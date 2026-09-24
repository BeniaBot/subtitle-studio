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

        /// <summary>שפת ברירת המחדל בהתקנה חדשה. עברית אם יש **סימן כלשהו** לעברית:
        /// ווינדוס בעברית, תבנית אזורית של ישראל, או מקלדת עברית. בארץ ווינדוס באנגלית
        /// נפוץ מאוד, ועד 0.8.1 רק שפת ווינדוס נבדקה - משתמש כזה היה מקבל אנגלית.
        /// אנגלית רק במחשב שאין בו שום עברית. ובכל מקרה - אפשר לשנות בהגדרות.</summary>
        public static string Detect()
        {
            try
            {
                if (IsHe(System.Globalization.CultureInfo.CurrentUICulture)) return He;
                if (IsHe(System.Globalization.CultureInfo.InstalledUICulture)) return He;
                if (IsHe(System.Globalization.CultureInfo.CurrentCulture)) return He;
                if (System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName == "IL") return He;
                foreach (System.Windows.Forms.InputLanguage il in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
                    if (IsHe(il.Culture)) return He;
            }
            catch { return He; }
            return En;
        }

        private static bool IsHe(System.Globalization.CultureInfo c)
        {
            if (c == null) return false;
            string n = c.TwoLetterISOLanguageName;
            return n == "he" || n == "iw";
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
