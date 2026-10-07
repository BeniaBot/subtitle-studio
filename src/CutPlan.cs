using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace SubtitleStudio
{
    /// <summary>קטע בחלון החיתוך: לשמור או להסיר, מ-A עד B (מילישניות).</summary>
    internal class CutSection
    {
        public int Id;
        public long A, B;
        public bool Keep = true;
        public Color Color;

        public long Length { get { return B - A; } }
    }

    /// <summary>הלוגיקה של החיתוך, בלי ממשק: מקטעים לטווחים שנשמרים, לכתוביות, ולפקודות של המנוע.
    ///
    /// **בהשראת ״חותך שמע״** (github.com/tsoolgee/audio-cutter, ‏MIT, באישור המפתח): קטעים לשמירה ולהסרה
    /// באותה פעולה. **התוצאה** = איחוד הקטעים לשמירה (או כל הקובץ, אם אין כאלה) פחות איחוד הקטעים להסרה.
    /// כך ״לשמור את 1:00-3:00 ולהסיר מתוכו את 1:20-1:25״ הוא פעולה אחת.
    ///
    /// **בלי קידוד מחדש כשאפשר:** קובץ קול (MP3, ‏M4A, ‏WAV, ‏FLAC...) נחתך תמיד בהעתקה - כל קטע לקובץ
    /// זמני, ואז חיבור (‏concat) - בפורמט המקורי. שם הכלי שמר כל פורמט שאינו MP3/WAV כ-WAV, ו-10 שניות
    /// של M4A יצאו 1.7MB. וידאו: ״מהיר״ מעתיק מפריים מפתח, ״מדויק״ מקודד מחדש.</summary>
    internal static class CutPlan
    {
        /// <summary>קטעים לשמירה בצבעים קרים, להסרה בחמים - אי אפשר להתבלבל ביניהם.</summary>
        internal static readonly Color[] KeepColors =
        {
            Rgb(0x2f9e44), Rgb(0x1c7ed6), Rgb(0x0c8599), Rgb(0x7048e8), Rgb(0x5c940d), Rgb(0x3b5bdb), Rgb(0x1098ad), Rgb(0x2b8a3e)
        };
        internal static readonly Color[] RemoveColors =
        {
            Rgb(0xe03131), Rgb(0xe8590c), Rgb(0xd6336c), Rgb(0xa0522d), Rgb(0xf08c00), Rgb(0xc2255c), Rgb(0xd9480f), Rgb(0x862e9c)
        };

        private static Color Rgb(int v) { return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255); }

        /// <summary>צבע פנוי מהסוג הנכון.</summary>
        internal static Color PickColor(List<CutSection> all, bool keep, CutSection except)
        {
            Color[] pal = keep ? KeepColors : RemoveColors;
            foreach (Color c in pal)
            {
                bool used = false;
                foreach (CutSection s in all) if (s != except && s.Color == c) { used = true; break; }
                if (!used) return c;
            }
            return pal[all.Count % pal.Length];
        }

        /// <summary>איחוד הקטעים מסוג אחד, ממוין.</summary>
        internal static List<long[]> Merged(List<CutSection> all, bool keep)
        {
            List<long[]> r = new List<long[]>();
            foreach (CutSection s in all) if (s.Keep == keep && s.B - s.A > 0) r.Add(new long[] { s.A, s.B });
            r.Sort(delegate (long[] x, long[] y) { return x[0].CompareTo(y[0]); });
            List<long[]> outp = new List<long[]>();
            foreach (long[] x in r)
            {
                if (outp.Count > 0 && x[0] <= outp[outp.Count - 1][1]) outp[outp.Count - 1][1] = Math.Max(outp[outp.Count - 1][1], x[1]);
                else outp.Add(new long[] { x[0], x[1] });
            }
            return outp;
        }

        /// <summary>טווחים פחות טווחים (שניהם ממוינים).</summary>
        internal static List<long[]> Subtract(List<long[]> ranges, List<long[]> cut)
        {
            List<long[]> outp = new List<long[]>();
            foreach (long[] r in ranges)
            {
                long a = r[0], b = r[1];
                foreach (long[] c in cut)
                {
                    if (c[1] <= a || c[0] >= b) continue;
                    if (c[0] > a) outp.Add(new long[] { a, c[0] });
                    a = Math.Max(a, c[1]);
                    if (a >= b) break;
                }
                if (b - a >= MinPartMs) outp.Add(new long[] { a, b });
            }
            return outp;
        }

        /// <summary>חלק קצר מזה הוא שארית של עיגול, לא קטע.</summary>
        internal const long MinPartMs = 20;

        internal static bool HasKeep(List<CutSection> all)
        {
            // קטע ריק (I בלי O) לא נחשב: אחרת הגל היה מעומעם כולו, והתוצאה - כל הקובץ
            foreach (CutSection s in all) if (s.Keep && s.B > s.A) return true;
            return false;
        }

        /// <summary>מה ייצא: הקטעים לשמירה (או כל הקובץ) פחות הקטעים להסרה.</summary>
        internal static List<long[]> Kept(List<CutSection> all, long durationMs)
        {
            List<long[]> keep = Merged(all, true);
            if (keep.Count == 0) keep.Add(new long[] { 0, durationMs });
            return Subtract(keep, Merged(all, false));
        }

        /// <summary>קובץ לכל קטע לשמירה (לפי הסדר בזמן), פחות מה שמסומן להסרה בתוכו.</summary>
        internal static List<List<long[]>> SplitItems(List<CutSection> all) { return SplitItems(all, null); }

        /// <summary><paramref name="numbers"/>: לכל קובץ - המספר של הקטע שלו בטבלה (1 והלאה). הקבצים נקראים
        /// לפיו, כך ש״קטע 3״ בתיקייה הוא השורה השלישית - גם כשהקטעים לא נוספו לפי הסדר בזמן.</summary>
        internal static List<List<long[]>> SplitItems(List<CutSection> all, List<int> numbers)
        {
            List<CutSection> keep = new List<CutSection>();
            foreach (CutSection s in all) if (s.Keep && s.B - s.A > 0) keep.Add(s);
            keep.Sort(delegate (CutSection x, CutSection y) { return x.A.CompareTo(y.A); });
            List<long[]> remove = Merged(all, false);
            List<List<long[]>> outp = new List<List<long[]>>();
            foreach (CutSection s in keep)
            {
                List<long[]> r = Subtract(new List<long[]> { new long[] { s.A, s.B } }, remove);
                if (r.Count == 0) continue;
                outp.Add(r);
                if (numbers != null) numbers.Add(all.IndexOf(s) + 1);
            }
            return outp;
        }

        /// <summary>האם הרגע הזה נשאר בתוצאה (לציור, ולדילוג בניגון).</summary>
        internal static bool IsKept(List<CutSection> all, long t)
        {
            bool inKeep = false;
            foreach (CutSection s in all)
                if (t >= s.A && t < s.B)
                {
                    if (!s.Keep) return false;
                    inKeep = true;
                }
            return inKeep || !HasKeep(all);
        }

        internal static long Total(List<long[]> ranges)
        {
            long n = 0;
            foreach (long[] r in ranges) n += r[1] - r[0];
            return n;
        }

        /// <summary>קביעת קצה, בלי שהקטע יתהפך או יתכווץ: התחלה שנקבעה אחרי הסוף - הקטע **זז** לשם ושומר על
        /// אורכו (וכך גם סוף לפני ההתחלה). ב״חותך שמע״ הם התחלפו (12-14 + ״1:05״ ← 14-65), והקטע התכווץ לאפס
        /// בלחיצה הבאה על חץ; ו-I אחרי סוף הקטע ״נתקע״ בסוף הישן.</summary>
        internal static void SetEdge(CutSection s, bool start, long t, long durationMs)
        {
            t = Math.Max(0, Math.Min(durationMs, t));
            long len = Math.Max(0, s.B - s.A);
            if (start)
            {
                if (t <= s.B) s.A = t;
                else { s.A = t; s.B = Math.Min(durationMs, t + len); }
            }
            else
            {
                if (t >= s.A) s.B = t;
                else { s.B = t; s.A = Math.Max(0, t - len); }
            }
            // **לא משאירים קטע ריק** (התחלה על הסוף, קטע בלי אורך, זמן מעבר לסוף הקובץ): שנייה מהקצה שנקבע -
            // ובקצה של הקובץ, לכיוון השני. קטע ריק לא נראה על הגל ולא חותך כלום.
            if (s.B <= s.A)
            {
                if (start) { s.B = Math.Min(durationMs, s.A + 1000); if (s.B <= s.A) s.A = Math.Max(0, s.B - 1000); }
                else { s.A = Math.Max(0, s.B - 1000); if (s.B <= s.A) s.B = Math.Min(durationMs, s.A + 1000); }
            }
        }

        // ---------- כתוביות ----------

        /// <summary>הכתוביות אחרי החיתוך: כל אחת זזה אחורה בכמה שהוסר לפניה; מה שבתוך חלק שהוסר - יורד;
        /// כתובית שנחתכת - נשאר מה שבתוך החלק שנשאר (וכתובית שחוצה חלק שהוסר - שני חלקים).</summary>
        internal static List<Cue> MapCues(List<Cue> cues, List<long[]> kept)
        {
            List<Cue> outp = new List<Cue>();
            long offset = 0;
            foreach (long[] r in kept)
            {
                foreach (Cue c in cues)
                {
                    if (c.End <= r[0] || c.Start >= r[1]) continue;
                    Cue n = c.Clone();
                    n.Start = Math.Max(r[0], c.Start) - r[0] + offset;
                    n.End = Math.Min(r[1], c.End) - r[0] + offset;
                    if (n.End - n.Start >= 60) outp.Add(n);
                }
                offset += r[1] - r[0];
            }
            outp.Sort(delegate (Cue x, Cue y) { return x.Start.CompareTo(y.Start); });
            return outp;
        }

        // ---------- המנוע ----------

        /// <summary>קובץ קול (בלי תמונה של ממש - תמונת עטיפה לא נחשבת).</summary>
        internal static bool AudioOnly(MediaInfo mi)
        {
            return mi != null && mi.FirstVideo() == null;
        }

        /// <summary>איך נחתך קובץ קול.</summary>
        internal enum AudioCut { Copy, Lossless, Lossy }

        /// <summary>**העתקה רק כשהיא באמת מדויקת.** MP3, ‏AAC, ‏WAV, ‏WMA - העתקה, בלי קידוד. FLAC (וכל קידוד בלי אובדן
        /// אחר) - קידוד מחדש ל-FLAC: מדויק, ובלי אובדן. OGG/OPUS וכל השאר - קידוד מחדש באיכות גבוהה.
        /// נמדד ב-real-sweep (7.10): העתקה של FLAC השאירה בכותרת את האורך של המקור (8 שניות ״באורך״ 1:34), ושל OGG -
        /// חיבור שאיבד 21 שניות; כל חיבור של OPUS הוסיף שליש שנייה.</summary>
        internal static AudioCut AudioWay(MediaInfo mi)
        {
            string c = AudioCodec(mi);
            if (c == "mp3" || c == "mp2" || c == "aac" || c == "ac3" || c == "eac3" || c == "alac" ||
                c.StartsWith("pcm_") || c.StartsWith("adpcm_") || c.StartsWith("wma")) return AudioCut.Copy;
            if (c == "flac" || c == "ape" || c == "wavpack" || c == "tta" || c == "mlp" || c == "truehd") return AudioCut.Lossless;
            return AudioCut.Lossy;
        }

        private static string AudioCodec(MediaInfo mi)
        {
            if (mi != null) foreach (MediaStream s in mi.Streams) if (s.Type == "audio") return (s.Codec ?? "").ToLowerInvariant();
            return "";
        }

        /// <summary>הקידוד של קובץ קול שלא נחתך בהעתקה, והסיומת שמתאימה לו: באותו פורמט כשאפשר.</summary>
        internal static string AudioCodecArgs(MediaInfo mi, out string ext)
        {
            string src = Path.GetExtension(mi.Path).ToLowerInvariant();
            string c = AudioCodec(mi);
            // ‏FLAC אחרי atrim: בלי גודל מסגרת קבוע המקודד נופל על מסגרת ראשונה של דגימה אחת (״invalid block size: 1״)
            if (AudioWay(mi) == AudioCut.Lossless) { ext = ".flac"; return "-c:a flac -frame_size 4608"; }
            if (c == "vorbis") { ext = src == ".oga" || src == ".mka" || src == ".webm" ? src : ".ogg"; return "-c:a libvorbis -q:a 6"; }
            if (c == "opus") { ext = src == ".ogg" || src == ".mka" || src == ".webm" ? src : ".opus"; return "-c:a libopus -b:a 160k"; }
            ext = ".m4a";
            return Q.MaxAudio;
        }

        /// <summary>האם השמירה תהיה בלי קידוד מחדש.</summary>
        internal static bool Copies(MediaInfo mi, bool fast)
        {
            return AudioOnly(mi) ? AudioWay(mi) == AudioCut.Copy : fast;
        }

        /// <summary>הסיומת של מה שנכתב: העתקה - כמו המקור; קול בקידוד - לפי הקידוד; וידאו - ‏MP4 כשהמיכל לא מקבל H.264.</summary>
        internal static string FinalPath(MediaInfo mi, string outPath, bool fast)
        {
            if (string.IsNullOrEmpty(outPath)) return outPath;
            if (Copies(mi, fast)) return outPath;
            if (!AudioOnly(mi)) return Burn.ReencodePath(outPath);
            string ext;
            AudioCodecArgs(mi, out ext);
            return string.Equals(Path.GetExtension(outPath), ext, StringComparison.OrdinalIgnoreCase) ? outPath : Path.ChangeExtension(outPath, ext);
        }

        /// <summary>תמונת העטיפה של קובץ קול נשארת (מקלט <paramref name="input"/>), במיכל שמחזיק אותה.</summary>
        private static string Cover(MediaInfo mi, string outPath, int input)
        {
            string e = Path.GetExtension(outPath).ToLowerInvariant();
            if (e != ".mp3" && e != ".m4a" && e != ".flac") return "";
            foreach (MediaStream s in mi.Streams)
                if (s.Type == "video") return "-map " + input.ToString(CultureInfo.InvariantCulture) + ":v:0 -c:v copy -disposition:v:0 attached_pic ";
            return "";
        }

        /// <summary>העבודה לקובץ אחד מהטווחים, בלי להריץ. <paramref name="tag"/> מבדיל בין קובצי ביניים של
        /// כמה קבצים באותה עבודה.</summary>
        internal static List<string> Steps(MediaInfo mi, List<long[]> ranges, string outPath, bool fast, string tmpDir, string tag)
        {
            List<string> steps = new List<string>();
            if (ranges == null || ranges.Count == 0) return steps;
            string src = Ff.Q(mi.Path);
            bool audioOnly = AudioOnly(mi);
            if (Copies(mi, fast))
            {
                // בחלקים - בלי תמונת העטיפה (היא ״ערוץ וידאו״ של פריים אחד, והחיבור נשבר עליה); היא והתגיות (שם,
                // אמן) חוזרות מהמקור בשלב החיבור
                string map = audioOnly ? "-map 0:a " : "";
                string tail = "-c copy -avoid_negative_ts make_zero ";
                if (ranges.Count == 1)
                {
                    steps.Add(Burn.CopyInputFlags(mi) + "-ss " + Tc.Ff(ranges[0][0]) + " -to " + Tc.Ff(ranges[0][1]) + " -i " + src + " " +
                              map + (audioOnly ? Cover(mi, outPath, 0) : "") + tail + Ff.Q(outPath));
                    return steps;
                }
                string ext = Path.GetExtension(outPath);
                if (string.IsNullOrEmpty(ext)) ext = Path.GetExtension(mi.Path);
                StringBuilder list = new StringBuilder();
                for (int i = 0; i < ranges.Count; i++)
                {
                    // שם קצר באנגלית בתיקייה זמנית - בלי בעיות של מרכאות ועברית ברשימת החיבור
                    string part = Path.Combine(tmpDir, "cut" + tag + "_" + i.ToString(CultureInfo.InvariantCulture) + ext);
                    steps.Add(Burn.CopyInputFlags(mi) + "-ss " + Tc.Ff(ranges[i][0]) + " -to " + Tc.Ff(ranges[i][1]) + " -i " + src + " " +
                              map + tail + Ff.Q(part));
                    list.Append("file '").Append(part.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
                }
                string listPath = Path.Combine(tmpDir, "cut" + tag + ".txt");
                File.WriteAllText(listPath, list.ToString(), new UTF8Encoding(false));
                // פרקים (ספר מוקלט, MKV) - בזמנים של המקור, שכבר לא נכונים: לא נכנסים
                steps.Add("-f concat -safe 0 -i " + Ff.Q(listPath) + " " +
                          (audioOnly ? "-i " + src + " -map 0:a " + Cover(mi, outPath, 1) + "-map_metadata 1 " : "") +
                          "-map_chapters -1 -c copy " + Ff.Q(outPath));
                return steps;
            }

            if (audioOnly)
            {
                // קול בקידוד מחדש: כל טווח נחתך בשרשרת, והכול מתחבר (גם טווח אחד - concat=n=1)
                string ext;
                string codec = AudioCodecArgs(mi, out ext);
                StringBuilder af = new StringBuilder();
                StringBuilder apads = new StringBuilder();
                for (int i = 0; i < ranges.Count; i++)
                {
                    string n = i.ToString(CultureInfo.InvariantCulture);
                    af.Append("[0:a]atrim=start=").Append(Tc.Ff(ranges[i][0])).Append(":end=").Append(Tc.Ff(ranges[i][1])).Append(",asetpts=PTS-STARTPTS[a").Append(n).Append("];");
                    apads.Append("[a").Append(n).Append("]");
                }
                af.Append(apads).Append("concat=n=").Append(ranges.Count.ToString(CultureInfo.InvariantCulture)).Append(":v=0:a=1[a]");
                steps.Add("-i " + src + " -filter_complex \"" + af + "\" -map \"[a]\" " + Cover(mi, outPath, 0) + codec + " -map_chapters -1 " + Ff.Q(outPath));
                return steps;
            }

            // וידאו, מדויק: פקודה אחת - כל טווח נחתך בשרשרת, והכול מתחבר
            bool audio = mi.HasAudio;
            // הערוץ של הסרט עצמו, לא ״הראשון״: בקובץ שתמונת עטיפה קודמת לו, ‎[0:v]‎ היה חותך את התמונה
            MediaStream fv = mi.FirstVideo();
            string vin = fv != null ? "[0:" + fv.Index.ToString(CultureInfo.InvariantCulture) + "]" : "[0:v]";
            StringBuilder f = new StringBuilder();
            StringBuilder pads = new StringBuilder();
            for (int i = 0; i < ranges.Count; i++)
            {
                string n = i.ToString(CultureInfo.InvariantCulture);
                f.Append(vin).Append("trim=start=").Append(Tc.Ff(ranges[i][0])).Append(":end=").Append(Tc.Ff(ranges[i][1])).Append(",setpts=PTS-STARTPTS[v").Append(n).Append("];");
                pads.Append("[v").Append(n).Append("]");
                if (audio)
                {
                    f.Append("[0:a]atrim=start=").Append(Tc.Ff(ranges[i][0])).Append(":end=").Append(Tc.Ff(ranges[i][1])).Append(",asetpts=PTS-STARTPTS[a").Append(n).Append("];");
                    pads.Append("[a").Append(n).Append("]");
                }
            }
            f.Append(pads).Append("concat=n=").Append(ranges.Count.ToString(CultureInfo.InvariantCulture)).Append(":v=1:a=").Append(audio ? "1" : "0").Append("[v]");
            if (audio) f.Append("[a]");
            StringBuilder sb = new StringBuilder();
            sb.Append("-i ").Append(src).Append(" -filter_complex \"").Append(f).Append("\" -map \"[v]\" ");
            if (audio) sb.Append("-map \"[a]\" ");
            sb.Append(Q.MaxVideo).Append(" ");
            if (audio) sb.Append(Q.MaxAudio).Append(" ");
            if (Path.GetExtension(outPath).ToLowerInvariant() == ".mp4") sb.Append("-movflags +faststart ");
            sb.Append("-map_chapters -1 ").Append(Ff.Q(outPath));
            steps.Add(sb.ToString());
            return steps;
        }

        /// <summary>העבודה כולה: קובץ אחד, או קובץ לכל פריט (<paramref name="outPaths"/> באותו סדר).</summary>
        internal static FfJob BuildJob(MediaInfo mi, List<List<long[]>> items, List<string> outPaths, bool fast)
        {
            FfJob job = new FfJob();
            string dir = Ff.TempDir();
            List<string> steps = new List<string>();
            List<string> names = new List<string>();
            long total = 0;
            for (int k = 0; k < items.Count && k < outPaths.Count; k++)
            {
                List<string> s = Steps(mi, items[k], outPaths[k], fast, dir, k.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < s.Count; i++)
                    names.Add(items.Count > 1 ? Lang.F("קובץ {0} מתוך {1}", k + 1, items.Count) : Lang.T("חותך"));
                steps.AddRange(s);
                total = Math.Max(total, Total(items[k]));
            }
            job.Steps = steps.ToArray();
            job.StepNames = names.ToArray();
            job.Args = steps.Count > 0 ? steps[0] : "";
            job.WorkDir = dir;
            job.TotalMs = Math.Max(1, total);
            job.OutputPath = outPaths.Count > 0 ? outPaths[0] : null;
            job.Title = items.Count > 1 ? Lang.F("שומר {0} קבצים", items.Count) : Lang.T("חותך ושומר");
            if (items.Count > 1 && job.OutputPath != null)
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(job.OutputPath).TrimEnd('\\'));
                if (string.IsNullOrEmpty(folder)) folder = Path.GetDirectoryName(job.OutputPath);
                job.SavedNote = Lang.F("נשמרו {0} קבצים, בתיקייה {1}", Theme.Ltr(items.Count.ToString(CultureInfo.InvariantCulture)), Theme.FileName(folder));
            }
            return job;
        }
    }
}
