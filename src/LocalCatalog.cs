using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SubtitleStudio
{
    /// <summary>מה המחשב הזה מסוגל להריץ (0.8.46, הרתמה למודלים מקומיים).
    ///
    /// **רוב המשתמשים עם מחשב חלש בהרבה משל בנימין** (בנימין, 16.11): 4 ליבות ישנות, 8GB זיכרון שמהם פנויים 3-4,
    /// בלי כרטיס מסך. התוכנה לא מניחה כלום - היא שואלת את ווינדוס, וליד כל מודל אומרת ״מתאים״ או ״כבד למחשב הזה״.
    /// לא חוסמים: מי שרוצה מודל כבד מוריד אותו, אבל יודע מראש.</summary>
    internal static class MachineInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemStatus
        {
            public uint Length, Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtended;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemStatus m);

        [DllImport("kernel32.dll")]
        private static extern bool IsProcessorFeaturePresent(int feature);

        /// <summary>לבדיקות: מחשב מדומה (זיכרון כולל ופנוי במגה, ליבות). ‏-1 = האמיתי.</summary>
        internal static long FakeTotalMB = -1, FakeFreeMB = -1;
        internal static int FakeCores = -1;

        public static long TotalMB { get { long t, f; Read(out t, out f); return t; } }
        /// <summary>פנוי **עכשיו** - משתנה כל רגע (דפדפן שנפתח). להחלטה ״מתאים / כבד״ משתמשים ב-`Budget`.</summary>
        public static long FreeMB { get { long t, f; Read(out t, out f); return f; } }

        private static void Read(out long total, out long free)
        {
            total = 0; free = 0;
            if (FakeTotalMB >= 0) { total = FakeTotalMB; free = FakeFreeMB >= 0 ? FakeFreeMB : FakeTotalMB / 2; return; }
            try
            {
                MemStatus m = new MemStatus();
                m.Length = (uint)Marshal.SizeOf(typeof(MemStatus));
                if (GlobalMemoryStatusEx(ref m))
                {
                    total = (long)(m.TotalPhys / (1024 * 1024));
                    free = (long)(m.AvailPhys / (1024 * 1024));
                }
            }
            catch { }
        }

        public static int Cores { get { return FakeCores > 0 ? FakeCores : Environment.ProcessorCount; } }

        /// <summary>AVX2 - בלעדיו מנועי המודלים רצים בגרסה האיטית שלהם (מעבד מלפני 2013 בערך).</summary>
        public static bool Avx2
        {
            get
            {
                try { return IsProcessorFeaturePresent(40); }        // PF_AVX2_INSTRUCTIONS_AVAILABLE
                catch { return false; }
            }
        }

        /// <summary>כמה זיכרון מודל יכול לתפוס בלי שהמחשב ייתקע: הגבוה מבין ״מה שפנוי עכשיו״ ו״הכולל פחות 4GB״
        /// (ווינדוס, הדפדפן והתוכנה עצמה), ולא יותר מחצי מהכולל. במחשב של 8GB - כ-4GB; של 16GB - 8GB.</summary>
        public static long BudgetMB
        {
            get
            {
                long total, free;
                Read(out total, out free);
                if (total <= 0) return 0;
                long b = Math.Max(free, total - 4096);
                return Math.Min(b, total / 2);
            }
        }
    }

    /// <summary>יכולת של התוכנה שמודל מקומי יכול לשרת.</summary>
    internal enum ModelTask { Transcribe, Translate, Assistant, Punctuate, Engine }

    /// <summary>קל / מאוזן / כבד - מה שהמשתמש בוחר בכפתורים ״רק הקלים״ / ״הכול״ / ״רק המדויקים״.</summary>
    internal enum ModelTier { Light, Balanced, Heavy }

    /// <summary>חבילה אחת בקטלוג: מודל (או מנוע) שאפשר להוריד. ״מה שנמדד״ - נמדד באמת, ובמחשב של בנימין
    /// (Core Ultra 5 125U, ‏16GB); ``docs/RESEARCH-local-models.md``.</summary>
    internal class ModelPack
    {
        public string Id = "";
        public ModelTask Task;
        public ModelTier Tier;
        /// <summary>השם שהמשתמש רואה, ושורה אחת: במה הוא טוב ובמה לא. דרך `Lang.T` במקום שמציג אותם.</summary>
        public string Title = "", Line = "";
        /// <summary>שפות: ״he״, ״en-he״, ״*״ = כל השפות שהמודל יודע.</summary>
        public string Langs = "";
        public long DiskMB, RamMB;
        /// <summary>רישיון, והערה כשיש בו מגבלה שהמשתמש צריך לדעת (לא מסחרי, טריטוריה).</summary>
        public string License = "", LicenseNote = "";
        /// <summary>מי פיתח ועל מה אומן - נאמר למשתמש לפני ההורדה (CC-BY מחייב קרדיט). קבוע, בלי `Lang.T` (שמות).</summary>
        public string Credit = "";
        /// <summary>חבילת מנוע משותפת שחייבת להיות לפני (``engine-sherpa``, ``engine-llama``), או ריק.</summary>
        public string Requires = "";
        /// <summary>אפשר להוריד כבר עכשיו. חבילה שעוד בבדיקה נשארת בקטלוג מוסתרת.</summary>
        public bool Available;

        /// <summary>איפה הקבצים: תגית המהדורה בגיטהאב (טרום הפצה), שם התיקייה תחת ``models``, וטבלת הקבצים -
        /// כמו ש-``build\make-local-stt.ps1`` מדפיס (שם, גודל, גודל דחוס, טביעה של הקובץ הפתוח).</summary>
        public string Tag = "", Folder = "";
        public string[] Files = new string[0], Sha256 = new string[0];
        public long[] Sizes = new long[0], GzSizes = new long[0];

        /// <summary>״מתאים למחשב הזה״ - הזיכרון שהמודל תופס בזמן ריצה בתוך התקציב (`MachineInfo.BudgetMB`).</summary>
        public bool Fits { get { long b = MachineInfo.BudgetMB; return b <= 0 || RamMB <= b; } }
    }

    /// <summary>כל המודלים שהתוכנה מכירה. **היום נמצאת בו חבילה אחת זמינה** - התמלול הקל של 0.8.45 (``LocalModel``);
    /// השאר נכנסים כשהם נמדדים על מחשב הייחוס (WORKPLAN 2לד). הערכים כאן - מדידות, לא ניחושים.</summary>
    internal static class ModelCatalog
    {
        private static List<ModelPack> _all;

        public static List<ModelPack> All
        {
            get
            {
                if (_all == null) _all = Build();
                return _all;
            }
        }

        private static ModelPack Pack(string id, ModelTask task, ModelTier tier, string title, string line, string langs,
                                      long disk, long ram, string license, string note, string requires, bool available)
        {
            ModelPack p = new ModelPack();
            p.Id = id; p.Task = task; p.Tier = tier; p.Title = title; p.Line = line; p.Langs = langs;
            p.DiskMB = disk; p.RamMB = ram; p.License = license; p.LicenseNote = note; p.Requires = requires; p.Available = available;
            return p;
        }

        private static List<ModelPack> Build()
        {
            List<ModelPack> r = new List<ModelPack>();
            // נמדד 15-16.11: 9.3% שגיאות על ״כאן״ (8.1% עם מילים מהרקע), פי 18 מזמן אמת על 2 ליבות, 179MB זיכרון
            r.Add(Pack("stt-he-fast", ModelTask.Transcribe, ModelTier.Light, Lang.T("תמלול עברית - קל ומהיר"),
                       Lang.T("שעה של שיעור בכמה דקות, גם במחשב חלש. טועה במילה בערך בכל משפט שני."), "he",
                       89, 180, "CC BY 4.0 · Apache 2.0", "", "", true));
            r[r.Count - 1].Credit = "Yakov Kolani · ivrit.ai · sherpa-onnx";
            // נמדד 16.11: 8.68% על ״כאן״, עם פיסוק, פי 4.5 על 2 ליבות; 1.37GB זיכרון על דקה וחצי - צריך חיתוך לקטעים קצרים
            r.Add(Pack("stt-he-parakeet", ModelTask.Transcribe, ModelTier.Balanced, Lang.T("תמלול עברית - מדויק יותר"),
                       Lang.T("פחות טעויות, ועדיין בלי להכביד: שעה של שיעור בכרבע שעה."), "he",
                       673, 1400, "CC BY 4.0", "", "engine-sherpa", false));
            r[r.Count - 1].Credit = "Yakov Kolani · ivrit.ai · NVIDIA Parakeet · sherpa-onnx";
            // המנוע של המתרגם הקל (17.11): פייתון מוטמע רשמי + CTranslate2 + SentencePiece + זמן הריצה של ++C, ב-ZIP אחד שנפרס.
            // לא מוצג בחלון - יורד לבד לפני מודל שצריך אותו. **גרסה חדשה = שם חדש לקובץ** (``engine-ct2-2.zip``): הסימן ``.ok``.
            r.Add(Pack("engine-ct2", ModelTask.Engine, ModelTier.Light, Lang.T("מנוע התרגום הקל"), "", "*",
                       81, 0, "PSF · MIT · Apache 2.0", "", "", true));
            ModelPack p = r[r.Count - 1];
            p.Credit = "Python · CTranslate2 · SentencePiece";
            p.Tag = "local-mt-1";
            p.Files = new string[] { "engine-ct2-1.zip" };
            p.Sizes = new long[] { 84396298 };
            p.GzSizes = new long[] { 30190370 };
            p.Sha256 = new string[] { "A68E434CE94E6132803F1149284CD90CF20A9681CCCE58C3BE8435372165D33F" };
            // נמדד 16.11: chrF++ 50.4 על WMT24++ (OPUS-MT 42.7), 0.05-0.08 שנ׳ לשורה על 2 ליבות. 17.11: המשקלים נשמרו ב-8 ביט
            // (כמו ש-CTranslate2 עושה בטעינה) - 410MB ← 211MB, ותרגום זהה ב-255 מתוך 255 משפטים
            r.Add(Pack("mt-en-he-quick", ModelTask.Translate, ModelTier.Light, Lang.T("תרגום מאנגלית לעברית - קל ומהיר"),
                       Lang.T("מתרגם מאות שורות בדקה. טוב במשפטים שלמים, פחות בשמות ובמונחים."), "en-he",
                       204, 400, "CC BY 4.0", "", "engine-ct2", false));
            p = r[r.Count - 1];
            p.Credit = "quickmt · CTranslate2";
            p.Tag = "local-mt-1";
            p.Files = new string[] { "config.json", "model.bin", "quickmt-en-he-LICENSE.txt", "source_vocabulary.json", "src.spm.model", "target_vocabulary.json", "tgt.spm.model" };
            p.Sizes = new long[] { 223, 210822367, 706, 545941, 802664, 1157365, 882724 };
            p.GzSizes = new long[] { 145, 188934113, 421, 144716, 404210, 165968, 403529 };
            p.Sha256 = new string[] {
                "66EDC5D58CEB812A41B4B784497001F815C35400884F40984B19DE29E1BC3D69",
                "AD18583B6F1C30E771F5EDBB2DFDA1D18762D12EA30113C45F4CCC8919E0ECD7",
                "68FA7BDD01418AD8579D88F5A74E76AFB923D05F3A61A8CB8F142203597D1164",
                "FF81562987774EABA94282D8737A4754322512995E909206BB19198D4267681C",
                "38AADFAE3DD591032D6E46DDCC38A7EC706E1F1429236CE9266882DBA6C3CE83",
                "E1BD37402EA3EA193D7116FA5FAEAEDB182EA7499A239BAD4D74472B8D7F5411",
                "006E2E0F144AE9425CF7FF6B81EB25DE0165DA5BFA8D220DFB75203AD9CA4EE4" };
            // לא נמדד עדיין (הקובץ הראשון היה פגום); כ-4.8GB זיכרון בריצה
            r.Add(Pack("mt-translategemma-4b", ModelTask.Translate, ModelTier.Heavy, Lang.T("תרגום לכל השפות - מדויק"),
                       Lang.T("מודל תרגום של גוגל. איטי יותר וצריך מחשב חזק."), "*",
                       2490, 4800, "Gemma", "", "engine-llama", false));
            r[r.Count - 1].Credit = "Google TranslateGemma · llama.cpp";
            // 30MB, רבע שנייה לכל התמלול; העלה את התרגום מ-36.4 ל-42.3 על כתוביות בלי פיסוק
            r.Add(Pack("punct-en", ModelTask.Punctuate, ModelTier.Light, Lang.T("פיסוק לאנגלית"),
                       Lang.T("מוסיף נקודות ופסיקים לטקסט אנגלי לפני התרגום - התרגום יוצא טוב יותר."), "en",
                       30, 100, "Apache 2.0", "", "engine-sherpa", false));
            r[r.Count - 1].Credit = "k2-fsa · sherpa-onnx";
            // Caspi (על Qwen3-ASR): לפי הכרטיס 4.2% על eval-d1; רישיון לא מסחרי - בנימין אישר (Subtext לא מסחרית)
            r.Add(Pack("stt-he-caspi", ModelTask.Transcribe, ModelTier.Heavy, Lang.T("תמלול עברית - הכי מדויק"),
                       Lang.T("המודל העברי המדויק ביותר שמצאנו. איטי, וצריך מחשב חזק."), "he",
                       1460, 2500, "CC BY-NC 4.0", Lang.T("לשימוש לא מסחרי בלבד."), "engine-llama", false));
            r[r.Count - 1].Credit = "OzLabs Caspi · Qwen3-ASR · ivrit.ai · llama.cpp";
            return r;
        }

        // ---------- התקנה, הורדה, מחיקה (לכל חבילה) ----------

        /// <summary>החבילה של 0.8.45 - יושבת ב-``models\stt-he`` עם המנוע בתוכה, והטבלה שלה ב-`LocalModel`. עד שהמנוע
        /// יפוצל לחבילה משלו (WORKPLAN 2לד), כל מה שנוגע בה עובר דרך `LocalModel` - כך גם הבדיקות הקיימות.</summary>
        internal const string LegacyId = "stt-he-fast";

        /// <summary>לבדיקות: תיקיית ``models`` אחרת.</summary>
        internal static string RootOverride;

        /// <summary>``models`` - האב של התיקייה של התמלול הקל (``%LOCALAPPDATA%\SubtitleStudio\models``, או ליד ה-EXE במצב נייד).</summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(RootOverride)) return RootOverride;
                return System.IO.Path.GetDirectoryName(LocalModel.Folder.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            }
        }

        public static string FolderOf(ModelPack p)
        {
            if (p.Id == LegacyId) return LocalModel.Folder;
            return System.IO.Path.Combine(Root, string.IsNullOrEmpty(p.Folder) ? p.Id : p.Folder);
        }

        /// <summary>כל הקבצים במקום - וגם המנוע שהיא צריכה.</summary>
        public static bool Installed(ModelPack p)
        {
            if (p == null) return false;
            if (p.Id == LegacyId) return LocalModel.Installed;
            if (p.Files.Length == 0) return false;
            if (!LocalModel.FilesOk(FolderOf(p), p.Files, p.Sizes)) return false;
            return string.IsNullOrEmpty(p.Requires) || Installed(ById(p.Requires));
        }

        /// <summary>כמה יורד בפועל, כולל מנוע שעוד לא הורד.</summary>
        public static long DownloadBytes(ModelPack p)
        {
            if (p.Id == LegacyId) return LocalModel.DownloadBytes;
            long n = 0;
            foreach (long g in p.GzSizes) n += g;
            ModelPack req = string.IsNullOrEmpty(p.Requires) ? null : ById(p.Requires);
            if (req != null && !Installed(req)) n += DownloadBytes(req);
            return n;
        }

        /// <summary>מוריד את החבילה - קודם את המנוע שהיא צריכה, אם חסר. ההתקדמות מצטברת על פני שתיהן.</summary>
        public static bool Download(ModelPack p, Action<long, long> progress, Func<bool> canceled, out string error)
        {
            error = null;
            if (p.Id == LegacyId) return LocalModel.Download(progress, canceled, out error);
            if (!LocalModel.Supported) { error = Lang.T("התמלול על המחשב צריך ווינדוס של 64 ביט."); return false; }
            long total = DownloadBytes(p), offset = 0;
            ModelPack req = string.IsNullOrEmpty(p.Requires) ? null : ById(p.Requires);
            if (req != null && !Installed(req))
            {
                long reqBytes = DownloadBytes(req);
                if (!Download(req, delegate (long d, long t) { if (progress != null) progress(d, total); }, canceled, out error)) return false;
                offset = reqBytes;
            }
            string url = "https://github.com/" + App.Repo + "/releases/download/" + p.Tag + "/";
            return LocalModel.DownloadFiles(UrlOverride ?? url, FolderOf(p), p.Files, p.Sizes, p.GzSizes, p.Sha256,
                delegate (long d, long t) { if (progress != null) progress(offset + d, total); }, canceled, out error);
        }

        /// <summary>לבדיקות: שרת מדומה במקום גיטהאב.</summary>
        internal static string UrlOverride;

        public static long SizeOnDisk(ModelPack p)
        {
            if (p.Id == LegacyId) return LocalModel.SizeOnDisk;
            long n = 0;
            try
            {
                string d = FolderOf(p);
                if (!System.IO.Directory.Exists(d)) return 0;
                foreach (string f in System.IO.Directory.GetFiles(d, "*", System.IO.SearchOption.AllDirectories))
                    try { n += new System.IO.FileInfo(f).Length; }
                    catch { }
            }
            catch { }
            return n;
        }

        /// <summary>מוחק רק את החבילה. **מנוע לא נמחק כל עוד חבילה מותקנת אחרת צריכה אותו** - מחזיר false עם הסבר.</summary>
        public static bool Remove(ModelPack p, out string error)
        {
            error = null;
            if (p.Id == LegacyId) return LocalModel.Remove(out error);
            foreach (ModelPack o in All)
                if (o != p && o.Requires == p.Id && Installed(o))
                {
                    error = Lang.F("המנוע הזה משמש את ״{0}״. מוחקים קודם אותו.", o.Title);
                    return false;
                }
            try
            {
                string d = FolderOf(p);
                if (System.IO.Directory.Exists(d)) System.IO.Directory.Delete(d, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ErrorText.Of(ex);
                Ai.Log("מחיקת " + p.Id + " נכשלה: " + ex.Message);
                return false;
            }
        }

        public static ModelPack ById(string id)
        {
            foreach (ModelPack p in All) if (p.Id == id) return p;
            return null;
        }

        /// <summary>החבילות שאפשר להוריד ליכולת, מהקלה לכבדה.</summary>
        public static List<ModelPack> For(ModelTask task)
        {
            List<ModelPack> r = new List<ModelPack>();
            foreach (ModelPack p in All) if (p.Task == task && p.Available) r.Add(p);
            r.Sort(delegate (ModelPack a, ModelPack b) { int c = a.Tier.CompareTo(b.Tier); return c != 0 ? c : a.RamMB.CompareTo(b.RamMB); });
            return r;
        }

        /// <summary>מה לכתוב ליד מודל: ״מתאים למחשב הזה״, או למה הוא כבד - במספרים שהמשתמש מבין.</summary>
        public static string FitText(ModelPack p)
        {
            if (p.Fits) return Lang.T("מתאים למחשב הזה");
            return Lang.F("כבד למחשב הזה: צריך כ-{0} זיכרון פנוי, ויש כ-{1}", Theme.Ltr(Gb(p.RamMB)), Theme.Ltr(Gb(MachineInfo.BudgetMB)));
        }

        private static string Gb(long mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " GB" : mb + " MB";
        }
    }
}
