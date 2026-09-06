# המתקין — מצב, ותכנון ערוץ העדכון

מסמך עבודה של תיקיית המתקין. נכתב אחרי סבב אימות מלא (‏6.9.2026).

---

## 1. מה קיים ונבדק

| קובץ | גודל | מקור |
|---|---|---|
| `dist\SubtitleStudio-Setup.exe` | ‏~36.6MB | `build\make-installer.cmd` |
| `build\installer\uninstall.exe` | ‏45,568 בתים | מוטמע במתקין, נפרס לתיקיית ההתקנה |
| `build\installer\version.txt` | 7 בתים | נגזר מ-`App.Version` שב-`src\Updater.cs` |

**סדר הבנייה חשוב:** `build.cmd` ואז `build\make-installer.cmd`.
המתקין מטמיע את `dist\SubtitleStudio.exe` כמשאב — בנייה של המתקין לפני
בניית התוכנה אורזת את הקובץ הישן, בלי שום אזהרה.

### מה נבדק בפועל

- התקנה שקטה (`/S /desktop /D="…"`) → קוד יציאה 0, ‏3 קבצים בתיקייה,
  שני קיצורים, ושני מפתחות רישום.
- הסרה שקטה (`uninstall.exe /S` וגם `SubtitleStudio-Setup.exe /uninstall /S /D=…`)
  → התיקייה, הקיצורים והרישום נעלמים; `%APPDATA%\SubtitleStudio\settings.ini`
  יוצא עם אותו ‏SHA-256 ואותו זמן שינוי.
- התקנה והסרה דרך הממשק, מונעות מקלדת בלבד.
- שלב הניקוי של המסיר (עותק זמני ב-`%TEMP%`) מוחק את עצמו — ‏`%TEMP%`
  נשאר נקי אחרי כמה שניות.

---

## 2. ערוץ העדכון — התכנון

הבעיה: מאותה מהדורה בגיטהאב אפשר להוריד שני דברים —
`SubtitleStudio-Setup.exe` ו-`SubtitleStudio.exe`. התוכנה צריכה לדעת
**מה היא** לפני שהיא בוחרת.

### 2.1 הסימן שהמתקין משאיר

**קובץ `installed.txt`, בתיקיית ההתקנה, ליד ה-EXE.**
נכתב ב-`Job.WriteMarker` (‏`src\Installer\Setup.cs`), ‏UTF-8:

```
installed=1
version=0.4.0
date=2026-09-06 06:08
dir=C:\Users\<שם>\AppData\Local\Programs\SubtitleStudio
size=38303744
channel=setup
```

**ובנוסף, רישום ב-HKCU** (‏`Job.WriteRegistry`) — גיבוי למקרה שמנקה
קבצים טרף את הסימן:

```
HKCU\Software\SubtitleStudio
    InstallLocation = <תיקיית ההתקנה>
    Version         = 0.4.0
    Channel         = setup
```

שניהם נמחקים בהסרה. `settings.ini` שב-`%APPDATA%` לא נוגעים בו לעולם,
אז ההעדפות שורדות הסרה והתקנה מחדש.

### 2.2 חוקי ההכרעה — לפי הסדר

1. **`portable.txt` ליד ה-EXE → נייד.** בחירה מפורשת של המשתמש,
   ו-`Runtime.cs` כבר מכבד אותה. גוברת על הכול.
2. **`installed.txt` ליד ה-EXE, וגם `dir=` שבו שווה לתיקייה שבה ה-EXE
   יושב עכשיו → מותקן.**
   ההשוואה הזאת היא כל העניין: מי שיעתיק את תיקיית ההתקנה לדיסק-און-קי
   ייקח איתו גם את הסימן, והוא ישקר. הנתיב שבתוכו לא ישקר.
3. **אחרת — `HKCU\Software\SubtitleStudio\InstallLocation` שווה לתיקייה
   הנוכחית → מותקן.**
4. **אחרת → נייד.**

### 2.3 הקוד — להוסיף ל-`src\Updater.cs`

```csharp
/// <summary>האם העותק שרץ הותקן, או שהוא קובץ בודד שמישהו הוריד.
/// המתקין משאיר installed.txt ליד ה-EXE ורישום ב-HKCU; שניהם נבדקים
/// מול התיקייה שבה ה-EXE באמת יושב, כי אפשר להעתיק תיקייה שלמה.</summary>
internal static class Install
{
    public const string Marker = "installed.txt";
    public const string RegApp = "Software\\SubtitleStudio";

    public static bool IsInstalled()
    {
        try
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);

            // בחירה מפורשת של המשתמש גוברת על כל סימן אחר
            if (File.Exists(Path.Combine(dir, "portable.txt"))) return false;

            string marker = Path.Combine(dir, Marker);
            if (File.Exists(marker))
            {
                foreach (string line in File.ReadAllLines(marker, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (!s.StartsWith("dir=", StringComparison.OrdinalIgnoreCase)) continue;
                    if (SameDir(s.Substring(4).Trim(), dir)) return true;
                    break;                       // הסימן קיים אבל מצביע למקום אחר
                }
            }

            using (Microsoft.Win32.RegistryKey k =
                   Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegApp))
            {
                if (k != null)
                {
                    string v = k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(v) && SameDir(v, dir)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool SameDir(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            a = Path.GetFullPath(a).TrimEnd('\\');
            b = Path.GetFullPath(b).TrimEnd('\\');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
```

`Updater.cs` לא מייבא את `Microsoft.Win32` ולא את `System.Globalization` —
לכן שם הטיפוס המלא בקוד למעלה. לא לשנות את רשימת ה-`using` אם אפשר להימנע.

### 2.4 לזהות את הנכס הנכון במהדורה

זה **חייב** להשתנות. היום `Updater.Check` לוקח את **הנכס הראשון**
שנגמר ב-`.exe`, ואת השדה `"size"` הראשון בכל ה-JSON. עם שני קבצים
במהדורה זו הגרלה.

```csharp
internal class Release
{
    public string Version = "";
    public string Url = "";          // ‏SubtitleStudio.exe — הקובץ הנייד
    public long Size;
    public string SetupUrl = "";     // ‏SubtitleStudio-Setup.exe
    public long SetupSize;
    public string Notes = "";
}
```

ובמקום שתי הלולאות הקיימות ב-`Check`:

```csharp
// ‏GitHub פולט לכל נכס name → size → browser_download_url בסדר הזה.
foreach (Match m in Regex.Matches(json,
    "\"name\"\\s*:\\s*\"([^\"]+\\.exe)\"[\\s\\S]{0,900}?\"size\"\\s*:\\s*(\\d+)" +
    "[\\s\\S]{0,900}?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
{
    string name = m.Groups[1].Value;
    string url = m.Groups[3].Value;
    if (url.IndexOf("/releases/download/", StringComparison.OrdinalIgnoreCase) < 0) continue;
    long sz = 0;
    long.TryParse(m.Groups[2].Value, out sz);
    if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
    { r.SetupUrl = url; r.SetupSize = sz; }
    else if (r.Url.Length == 0)
    { r.Url = url; r.Size = sz; }
}
```

התקרה `{0,900}` היא המגן: בלעדיה `[\s\S]*?` יכול לדלג מנכס אחד לשדה
של נכס אחר כשחסר שדה. הבדיקה על `/releases/download/` מוציאה מהמשחק
את שם המהדורה עצמה (שדה `"name"` ברמה העליונה).

### 2.5 מותקן — מעדכנים דרך המתקין

הרעיון: **לא נוגעים ב-`UpdateScript` בכלל.** המתקין עצמו יודע להמתין
לתהליך, להחליף את הקובץ, לרענן קיצורים, לכתוב את הגרסה החדשה לרישום
ולסימן, להפעיל מחדש ולמחוק את עצמו. כל הדגלים כבר קיימים ונבדקו.

```csharp
// ‏רק כשהעותק מותקן ויש נכס מתקין במהדורה
string dir = Path.GetDirectoryName(exe).TrimEnd('\\');
string args = "/S" +
              " /D=\"" + dir + "\"" +
              " /waitpid=" + Process.GetCurrentProcess().Id.ToString(
                                 System.Globalization.CultureInfo.InvariantCulture) +
              " /run" +
              " /cleanself";

ProcessStartInfo psi = new ProcessStartInfo(tmp, args);
psi.UseShellExecute = true;                  // כדי שה-manifest של המתקין ייקרא
psi.WorkingDirectory = Path.GetTempPath();
Process.Start(psi);
Application.Exit();
```

מה כל דגל עושה (‏`Options.Parse` ב-`src\Installer\Setup.cs`):

| דגל | מה הוא עושה |
|---|---|
| `/S` | בלי ממשק. **גם מכבה קיצור שולחן עבודה** — עדכון לא מוסיף קיצורים מיוזמתו |
| `/D="…"` | מתקין לאותה תיקייה שבה התוכנה כבר יושבת |
| `/waitpid=N` | ממתין עד 30 שניות שהתהליך ייסגר, ואז מחליף |
| `/run` | מפעיל את התוכנה מחדש בסיום |
| `/cleanself` | מסמן את המתקין שהורד למחיקה באתחול הבא (‏`MoveFileEx`) |

`TrimEnd('\\')` הוא חובה: `"C:\dir\"` בסוף שורת פקודה — הבקסלש מברִיח
את המרכאה, ו-`CommandLineToArgvW` בולע אותה.

**קוד היציאה של המתקין נבדק ומדויק** (`0` הצלחה, `1` שגיאה, `2` ביטול),
אבל התוכנה כבר סגורה בשלב הזה ואין מי שיקרא אותו. מי שכן קורא: יומן
`%TEMP%\substudio-setup.log`, שאליו המתקין כותב כל ריצה.

### 2.6 נייד — בדיוק כמו היום

מורידים את `rel.Url` (ה-EXE החשוף), כותבים
`Updater.UpdateScript(tmp, exe)` ב-`Encoding.ASCII` ומריצים ב-`cmd.exe`.
זה המסלול שבשבילו נבנו הנתיבים הקצרים ותקרת ה-15 ניסיונות, והוא נשאר
**רק** למסלול הזה. במצב מותקן אין קובץ אצווה בכלל — ולכן גם אין את כל
משפחת הבאגים של עברית ב-cmd.

### 2.7 קצוות שצריך להחליט עליהם

- **מותקן, אבל במהדורה אין נכס מתקין** → ליפול חזרה ל-EXE החשוף
  ול-`UpdateScript`. התוכנה תתעדכן, אבל `DisplayVersion` ברישום
  ו-`version=` בסימן יישארו על הגרסה הישנה. אם רוצים לסגור את זה,
  אחרי החלפה מוצלחת אפשר לרענן אותם מתוך התוכנה:

  ```csharp
  using (Microsoft.Win32.RegistryKey k =
         Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Install.RegApp))
      if (k != null) k.SetValue("Version", App.Version);
  ```

- **נייד, ובמהדורה יש רק מתקין** → לא להתקין בשקט. להציג הודעה עם
  קישור לדף המהדורה. מי שבחר קובץ בודד לא ביקש שיירשם לו משהו במחשב.
- **הרשאות** — אין. הכול תחת `%LOCALAPPDATA%` ו-HKCU, ‏`asInvoker`
  ב-`build\installer\setup.manifest`. עדכון של עותק שהותקן ידנית
  ל-`C:\Program Files` פשוט ייכשל בכתיבה, ו-`Job.Place` יחזיר שגיאה
  מסודרת. זה מקרה קצה שהמתקין הזה לא מתיימר לתמוך בו.

---

## 3. טלאים לקבצים שאסור לי לגעת בהם

### 3.1 `src\Updater.cs`

שלושה שינויים, כולם מפורטים למעלה:

1. להוסיף את המחלקה `Install` (סעיף 2.3).
2. להחליף את זיהוי הנכסים ב-`Check`, ולהוסיף `SetupUrl`/`SetupSize`
   ל-`Release` (סעיף 2.4).
3. ב-`DownloadAndApply`, אחרי שההורדה הצליחה — להתפצל:

```csharp
bool viaSetup = Install.IsInstalled() && !string.IsNullOrEmpty(rel.SetupUrl);
if (viaSetup)
{
    // ‏סעיף 2.5 - המתקין עושה הכול
}
else
{
    // הקוד הקיים: UpdateScript + cmd.exe
}
```

וב-`Updates.Offer`, לבחור מה מורידים לפני שקוראים ל-`DownloadAndApply`:

```csharp
bool inst = Install.IsInstalled();
string url = inst && rel.SetupUrl.Length > 0 ? rel.SetupUrl : rel.Url;
long size = inst && rel.SetupUrl.Length > 0 ? rel.SetupSize : rel.Size;
```

וגם: הודעת ההצעה אומרת היום ״התוכנה תיסגר ותיפתח מחדש לבד״ — זה נכון
בשני המסלולים, אז אין מה לשנות בטקסט.

### 3.2 `CLAUDE.md` — סעיף 7 (״פריסה ועדכונים״)

להוסיף:

```markdown
- **התקנה**: `build\make-installer.cmd` אורז את `dist\SubtitleStudio.exe`
  לתוך `dist\SubtitleStudio-Setup.exe` (‏WinForms רגיל, אותו csc מובנה).
  ההתקנה היא למשתמש בלבד: `%LOCALAPPDATA%\Programs\SubtitleStudio`,
  קיצור בתפריט התחל, ורישום ב-`HKCU\…\Uninstall\SubtitleStudio`.
  **להריץ תמיד אחרי `build.cmd`** — אחרת נארז EXE ישן.
- `installed.txt` ליד ה-EXE = הותקן דרך המתקין. `portable.txt` = נייד,
  וגובר. ‏`Updater` בוחר לפי זה אם להוריד את המתקין או את ה-EXE החשוף.
- שקט: `SubtitleStudio-Setup.exe /S [/D="…"] [/desktop] [/run] [/waitpid=N] [/cleanself]`
```

### 3.3 תהליך המהדורה

`gh release create` צריך להעלות **שני** נכסים:
`SubtitleStudio.exe` **וגם** `SubtitleStudio-Setup.exe`.
בלי הראשון עותקים ניידים מפסיקים להתעדכן; בלי השני מותקנים נופלים
למסלול הנייד ומשאירים רישום מיושן.

---

## 4. מה שונה בסבב הזה (בקבצים של תיקיית המתקין)

- **`Skin.cs`** — `FlatBtn` ו-`CheckLine` לא הגיבו למקלדת בכלל. הם יורשים
  מ-`Control` ולא מ-`ButtonBase`, ו-`Control` לא הופך רווח/אנטר ללחיצה.
  נוסף `IsInputKey`/`OnKeyDown`/`OnKeyUp`, טבעת מיקוד גלויה, ו-`Focus()`
  בלחיצת עכבר. ‏`Bar` הוצא מסדר ה-Tab.
- **`Skin.cs`** — `Path_()` חדש: נתיב בשורה אחת עם קיצור באמצע
  (`PathEllipsis`), ו-`Ltr()` לסימוני כיוון.
- **`SetupForm.cs` / `UninstallUi.cs`** — `ProcessCmdKey`: אנטר מפעיל את
  הכפתור הראשי, ‏Esc מבטל. ‏`AcceptButton` לא רלוונטי כאן כי הכפתורים
  אינם `IButtonControl`.
- **`SetupForm.cs`** — מסך הסיום הציג ״‏C:‎״ במקום הנתיב: המחרוזת נשברה
  לשורות ורק שתיים נכנסו למלבן, והשורה השנייה גם התנגשה עם תיבת הסימון
  ״להפעיל עכשיו״. עכשיו הנתיב בשורה נפרדת עם קיצור באמצע, והתיבה ירדה
  מ-‎196 ל-‎208.
- **`UninstallUi.cs`** — ״(‏98 MB)״ הוצג ״(‏MB 98)״. עטוף ב-`Skin.Ltr`.
