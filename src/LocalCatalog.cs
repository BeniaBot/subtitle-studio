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
    internal enum ModelTask { Transcribe, Translate, Assistant, Punctuate }

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
        /// <summary>חבילת מנוע משותפת שחייבת להיות לפני (``engine-sherpa``, ``engine-llama``), או ריק.</summary>
        public string Requires = "";
        /// <summary>אפשר להוריד כבר עכשיו. חבילה שעוד בבדיקה נשארת בקטלוג מוסתרת.</summary>
        public bool Available;

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
            r.Add(Pack("stt-he-fast", ModelTask.Transcribe, ModelTier.Light, "תמלול עברית - קל ומהיר",
                       "שעה של שיעור בכמה דקות, גם במחשב חלש. טועה במילה בערך בכל משפט שני.", "he",
                       89, 180, "CC BY 4.0 · Apache 2.0", "", "", true));
            // נמדד 16.11: 8.68% על ״כאן״, עם פיסוק, פי 4.5 על 2 ליבות; 1.37GB זיכרון על דקה וחצי - צריך חיתוך לקטעים קצרים
            r.Add(Pack("stt-he-parakeet", ModelTask.Transcribe, ModelTier.Balanced, "תמלול עברית - מדויק יותר",
                       "פחות טעויות, ועדיין בלי להכביד: שעה של שיעור בכרבע שעה.", "he",
                       673, 1400, "CC BY 4.0", "", "engine-sherpa", false));
            // נמדד 16.11: chrF++ ‏50.4 על WMT24++ (OPUS-MT ‏42.7), ‏0.05-0.08 שנ׳ לשורה על 2 ליבות; כ-400MB זיכרון (לא נמדד במדויק)
            r.Add(Pack("mt-en-he-quick", ModelTask.Translate, ModelTier.Light, "תרגום מאנגלית לעברית - קל ומהיר",
                       "מתרגם מאות שורות בדקה. טוב במשפטים שלמים, פחות בשמות ובמונחים.", "en-he",
                       410, 400, "CC BY 4.0", "", "", false));
            // לא נמדד עדיין (הקובץ הראשון היה פגום); כ-4.8GB זיכרון בריצה
            r.Add(Pack("mt-translategemma-4b", ModelTask.Translate, ModelTier.Heavy, "תרגום לכל השפות - מדויק",
                       "מודל תרגום של גוגל. איטי יותר וצריך מחשב חזק.", "*",
                       2490, 4800, "Gemma", "", "engine-llama", false));
            // 30MB, רבע שנייה לכל התמלול; העלה את התרגום מ-36.4 ל-42.3 על כתוביות בלי פיסוק
            r.Add(Pack("punct-en", ModelTask.Punctuate, ModelTier.Light, "פיסוק לאנגלית",
                       "מוסיף נקודות ופסיקים לטקסט אנגלי לפני התרגום - התרגום יוצא טוב יותר.", "en",
                       30, 100, "Apache 2.0", "", "engine-sherpa", false));
            // Caspi (על Qwen3-ASR): לפי הכרטיס 4.2% על eval-d1; רישיון לא מסחרי - בנימין אישר (Subtext לא מסחרית)
            r.Add(Pack("stt-he-caspi", ModelTask.Transcribe, ModelTier.Heavy, "תמלול עברית - הכי מדויק",
                       "המודל העברי המדויק ביותר שמצאנו. איטי, וצריך מחשב חזק.", "he",
                       1460, 2500, "CC BY-NC 4.0", "לשימוש לא מסחרי בלבד.", "engine-llama", false));
            return r;
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
