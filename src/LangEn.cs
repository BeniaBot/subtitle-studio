using System.Collections.Generic;

namespace SubtitleStudio
{
    /// <summary>GENERATED FROM build\lang\en.tsv BY build\make-lang.ps1.
    /// Do not edit by hand: edit the table and run the script.</summary>
    internal static class LangEn
    {
        private static readonly string[] P0 = new string[] {
            "פותחים סרט", "Open a video",
            "גרירה לחלון או ״עיון בקבצים״", "Drag it in, or click Browse",
            "כותבים כתוביות", "Write the subtitles",
            "או טוענים קובץ כתוביות קיים", "Or load a subtitle file you already have",
            "שומרים סרט חדש", "Save a new video",
            "עם הכתוביות בפנים, מוכן לשליחה", "Subtitles inside, ready to send",
            "עיון בקבצים", "Browse",
            "קובץ כתוביות", "Subtitle file",
            "איך עובדים כאן - מדריך קצר וקיצורי מקלדת (F1)", "How this works - a short guide and the shortcuts (F1)",
            "מעבר למצב בהיר", "Switch to light mode",
            "מעבר למצב כהה", "Switch to dark mode",
            "על התוכנה, מנוע הווידאו ועדכונים", "About, video engine and updates",
            "אולפן הכתוביות · ליצור, לתקן ולהטמיע כתוביות בעברית", "Create, fix and embed subtitles in any video",
            "גררו לכאן סרט או קובץ כתוביות", "Drop a video or a subtitle file here",
            "נפתחו לאחרונה", "Recent files",
            "· הקובץ לא נמצא", "· file not found",
        };

        public static void Fill(Dictionary<string, string> d)
        {
            Add(d, P0);
        }

        private static void Add(Dictionary<string, string> d, string[] a)
        {
            for (int i = 0; i + 1 < a.Length; i += 2) d[a[i]] = a[i + 1];
        }
    }
}
