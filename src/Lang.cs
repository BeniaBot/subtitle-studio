using System;
using System.Collections.Generic;

namespace SubtitleStudio
{
    /// <summary>שפת הממשק.
    ///
    /// ‏**המפתח הוא המחרוזת העברית עצמה.** אין קובץ משאבים ואין מפתחות
    /// מומצאים: הקוד ממשיך להיקרא בעברית, וכשהשפה אנגלית `T` מחזירה את
    /// התרגום מהטבלה. מחרוזת שאין לה תרגום חוזרת כמו שהיא - כלומר
    /// **בעברית**, ולא כמפתח ריק שמשאיר כפתור בלי כיתוב.
    ///
    /// הטבלה עצמה ב-`LangEn.cs`, שנוצר מ-`build\lang\en.tsv` על ידי
    /// `build\make-lang.ps1`. לא לערוך אותו ביד.</summary>
    internal static class Lang
    {
        public const string He = "he";
        public const string En = "en";

        private static string _code = He;
        private static Dictionary<string, string> _map;

        public static string Code { get { return _code; } }

        /// <summary>האם הממשק אנגלי. **זה לא אומר שהתוכן אנגלי** - כתובית
        /// עברית נשארת עברית גם בממשק אנגלי, ולהפך.</summary>
        public static bool IsEn { get { return _code == En; } }

        /// <summary>כיווניות ה**ממשק**. לא לבלבל עם כיווניות התוכן:
        /// ‏`Theme.RtlFix` ו-`Theme.FileName` תלויים במה שכתוב, לא בשפה.</summary>
        public static bool Rtl { get { return _code != En; } }

        public static void Set(string code)
        {
            _code = (code == En) ? En : He;
            _map = null;
        }

        /// <summary>שפת ברירת המחדל בהפעלה ראשונה, לפי שפת ווינדוס.
        /// עברית - עברית; כל השאר - אנגלית.</summary>
        public static string Detect()
        {
            try
            {
                string n = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (n == "he" || n == "iw") return He;
                n = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
                if (n == "he" || n == "iw") return He;
            }
            catch { return He; }
            return En;
        }

        private static Dictionary<string, string> Map
        {
            get
            {
                if (_map == null)
                {
                    _map = new Dictionary<string, string>(2048, StringComparer.Ordinal);
                    if (_code == En) LangEn.Fill(_map);
                }
                return _map;
            }
        }

        /// <summary>הטקסט שיוצג. נקרא גם מתוך ציור, ולכן חייב להיות זול.</summary>
        public static string T(string he)
        {
            if (_code == He || string.IsNullOrEmpty(he)) return he;
            string v;
            if (Map.TryGetValue(he, out v) && v.Length > 0) return v;
            return he;
        }

        /// <summary>תרגום ואז `string.Format`. **כך מרכיבים משפט עם מספר**,
        /// במקום לשרשר קטעים: סדר המילים באנגלית שונה, ושרשור מפרק את
        /// המשפט לחתיכות שאי אפשר לתרגם נכון.</summary>
        public static string F(string he, params object[] args)
        {
            string t = T(he);
            try { return string.Format(t, args); }
            catch { return t; }
        }

        /// <summary>האם יש תרגום. לשימוש הבדיקות בלבד.</summary>
        public static bool Has(string he)
        {
            if (string.IsNullOrEmpty(he)) return true;
            string v;
            return Map.TryGetValue(he, out v) && v.Length > 0;
        }

        /// <summary>מספר הערכים בטבלה. לשימוש הבדיקות בלבד.</summary>
        public static int Count { get { return Map.Count; } }
    }
}
