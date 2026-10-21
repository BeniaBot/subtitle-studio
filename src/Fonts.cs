using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SubtitleStudio
{
    /// <summary>
    /// טוען את גופן הממשק המוטמע (IBM Plex Sans Hebrew; עד 0.8.1 - Assistant) מתוך ה-EXE.
    /// חייבים שתי טעינות: PrivateFontCollection בשביל GDI+ (DrawString)
    /// ו-AddFontMemResourceEx בשביל GDI (TextRenderer). בלי השנייה
    /// כל טקסט אטום היה חוזר ל-Segoe UI וההבדל היה בולט.
    /// </summary>
    internal static class Fonts
    {
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetGlyphIndicesW(IntPtr hdc, string text, int count, [Out] ushort[] indices, uint flags);
        [DllImport("gdi32.dll")] private static extern int GetTextCharset(IntPtr hdc);

        private static readonly Dictionary<string, bool> _hebrew = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>**יש בגופן אותיות עבריות?** (0.8.28) בגופן בלי עברית (Consolas, Calibri...) גם התצוגה וגם הצריבה מחליפות את האותיות
        /// בגופן אחר - וכל אחת בגופן אחר (``hp-tools\style-compare.ps1``: רוחב 403 מול 387). לכן חלון העיצוב בממשק העברי מציע רק גופנים
        /// שיש בהם עברית. ‏GetGlyphIndices עם GGI_MARK_NONEXISTING_GLYPHS: אות שאין - 0xFFFF.</summary>
        public static bool HasHebrew(string family)
        {
            bool r;
            lock (_hebrew) { if (_hebrew.TryGetValue(family ?? "", out r)) return r; }
            r = false;
            IntPtr dc = IntPtr.Zero, hf = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                using (Font f = new Font(family, 20f, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
                    {
                        hf = f.ToHfont();
                        dc = CreateCompatibleDC(IntPtr.Zero);
                        old = SelectObject(dc, hf);
                        ushort[] idx = new ushort[4];
                        // גופן סמלים (Wingdings) ״ממפה״ כל תו לסמל - לא עברית
                        if (GetTextCharset(dc) != 2 && GetGlyphIndicesW(dc, "אבשת", 4, idx, 1) == 4)
                            r = idx[0] != 0xFFFF && idx[1] != 0xFFFF && idx[2] != 0xFFFF && idx[3] != 0xFFFF;
                    }
                }
            }
            catch { r = false; }
            finally
            {
                if (dc != IntPtr.Zero) { if (old != IntPtr.Zero) SelectObject(dc, old); DeleteDC(dc); }
                if (hf != IntPtr.Zero) DeleteObject(hf);
            }
            lock (_hebrew) _hebrew[family ?? ""] = r;
            return r;
        }

        private static PrivateFontCollection _pfc;
        private static readonly List<IntPtr> _pinned = new List<IntPtr>();
        private static FontFamily _regular, _semi, _bold;
        private static bool _tried;

        /// <summary>שם המשפחה לשימוש ב-TextRenderer. ריק אם הטעינה נכשלה.</summary>
        public static string FaceRegular = "";
        public static string FaceSemi = "";
        public static string FaceBold = "";

        public static bool Ready { get { Load(); return _regular != null; } }

        public static FontFamily Regular { get { Load(); return _regular; } }
        public static FontFamily Semi { get { Load(); return _semi != null ? _semi : _regular; } }
        public static FontFamily Bold { get { Load(); return _bold != null ? _bold : _regular; } }

        private static void Load()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                _pfc = new PrivateFontCollection();
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] data;
                    using (Stream s = asm.GetManifestResourceStream(name))
                    {
                        if (s == null) continue;
                        data = new byte[s.Length];
                        int got = 0;
                        while (got < data.Length)
                        {
                            int n = s.Read(data, got, data.Length - got);
                            if (n <= 0) break;
                            got += n;
                        }
                    }
                    IntPtr p = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, p, data.Length);
                    _pinned.Add(p);
                    _pfc.AddMemoryFont(p, data.Length);
                    uint installed;
                    AddFontMemResourceEx(p, (uint)data.Length, IntPtr.Zero, out installed);
                }

                foreach (FontFamily f in _pfc.Families)
                {
                    string n = f.Name;
                    // ״Medium״ נחשב חצי-מודגש: בגופנים שאין להם SemiBold (Heebo, Rubik)
                    // זה המשקל שבין הרגיל למודגש
                    if (n.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Semi Bold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0) _semi = f;
                    else if (n.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0) _bold = f;
                    else if (_regular == null) _regular = f;
                }
                // קובץ ה-Bold בדרך כלל מתמזג למשפחה אחת עם הרגיל - אז אין משפחה נפרדת
                if (_regular == null && _semi != null) { _regular = _semi; _semi = null; }
                if (_regular != null && _bold == null && _regular.IsStyleAvailable(FontStyle.Bold)) _bold = _regular;

                FaceRegular = _regular != null ? _regular.Name : "";
                FaceSemi = _semi != null ? _semi.Name : FaceRegular;
                FaceBold = _bold != null ? _bold.Name : FaceRegular;
            }
            catch
            {
                _regular = _semi = _bold = null;
                FaceRegular = FaceSemi = FaceBold = "";
            }
        }

        /// <summary>בונה גופן מהמשפחה המוטמעת, עם נפילה ל-Segoe UI אם משהו השתבש.</summary>
        public static Font Make(float size, FontStyle style)
        {
            Load();
            try
            {
                if (_regular != null)
                {
                    // המשקל הבינוני נותן את המראה הנקי; בולד אמיתי רק כשמבקשים
                    FontFamily fam = _regular;
                    FontStyle st = style;
                    if ((style & FontStyle.Bold) != 0)
                    {
                        if (_bold != null && _bold != _regular) { fam = _bold; st = style & ~FontStyle.Bold; }
                        else if (!_regular.IsStyleAvailable(FontStyle.Bold) && _semi != null) { fam = _semi; st = style & ~FontStyle.Bold; }
                    }
                    if (fam.IsStyleAvailable(st)) return new Font(fam, size, st, GraphicsUnit.Point);
                    if (fam.IsStyleAvailable(FontStyle.Regular)) return new Font(fam, size, FontStyle.Regular, GraphicsUnit.Point);
                }
            }
            catch { }
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        /// <summary>גופן חצי-מודגש - לכותרות משנה ולתוויות. נראה יקר יותר מבולד מלא.</summary>
        public static Font MakeSemi(float size)
        {
            Load();
            try
            {
                if (_semi != null && _semi.IsStyleAvailable(FontStyle.Regular))
                    return new Font(_semi, size, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch { }
            return Make(size, FontStyle.Bold);
        }
    }
}
