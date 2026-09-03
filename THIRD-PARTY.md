# רכיבי צד שלישי · Third-party components

## FFmpeg

התוכנה משתמשת ב‑**FFmpeg** כמנוע המדיה שלה. הקובץ `ffmpeg.exe` נכלל דחוס בתוך
ה‑EXE ונפרס לתיקיית המשתמש בהפעלה הראשונה. התוכנה מריצה אותו כתהליך נפרד
בשורת פקודה — היא לא מקשרת אליו קוד.

*Subtitle Studio uses **FFmpeg** as its media engine. A compressed copy of
`ffmpeg.exe` is embedded in the published EXE and unpacked to the user's folder on
first run. The app runs it as a separate process via the command line; it does not
link against it.*

| | |
|---|---|
| בילד · Build | `essentials_build` by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) |
| גרסה · Version | `9.0.1-essentials_build` |
| רישיון · License | **GNU General Public License v3** (`--enable-gpl --enable-version3`) |
| קוד מקור · Source | https://ffmpeg.org/download.html · https://git.ffmpeg.org/ffmpeg.git (commit `4d7c609be3`) |
| טקסט הרישיון · License text | https://www.gnu.org/licenses/gpl-3.0.html |

### הצעת קוד מקור · Written offer

בהתאם ל‑GPLv3: קוד המקור המלא של בילד ה‑FFmpeg המצורף זמין בקישורים שלמעלה.
מי שמעדיף לקבלו בדרך אחרת מוזמן לפתוח [Issue](../../issues) והוא יסופק.

*Per GPLv3, the complete corresponding source of the bundled FFmpeg build is
available at the links above. If you prefer another delivery method, open an
[Issue](../../issues) and it will be provided.*

### מה זה אומר בפועל · What this means

- **קוד התוכנה הזאת** — `src\*.cs`, `build.cmd`, הסקריפטים — הוא **MIT**.
- **קובץ ה‑EXE שבמהדורות** מכיל בנוסף בינארי GPLv3, ולכן ההפצה שלו כפופה ל‑GPLv3:
  מותר להוריד, להשתמש, להעתיק ולהפיץ הלאה — כל עוד מלווים אותו במידע שבדף הזה.
- מי שבונה מהמקור בעצמו מקבל תוכנה שאינה מכילה FFmpeg כלל (ראו למטה).

### בנייה בלי הבינארי · Building without the binary

הקובץ `tools\ffmpeg.exe` (‏194MB) לא נשמר במאגר. להורדה: [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)
→ `ffmpeg-release-full.7z` → להעתיק את `bin\ffmpeg.exe` אל `tools\`.

**חובה בילד עם `libass` + `libfribidi` + `libharfbuzz`** — בלעדיהם צריבת עברית
תצא הפוכה או ריקה. ה‑full build כולל אותם; ה‑essentials build לא בהכרח.

*`tools\ffmpeg.exe` (194MB) is not stored in the repository. Download the gyan.dev
full build and copy `bin\ffmpeg.exe` into `tools\`. The build **must** include
`libass`, `libfribidi` and `libharfbuzz`, or Hebrew burn-in comes out reversed.*

## רכיבים נוספים · Everything else

אין. אין NuGet, אין ספריות מצורפות, אין גופני אייקונים — כל האייקונים מצוירים בקוד,
והתוכנה מהודרת עם מהדר ה‑C# המובנה בווינדוס מול ‎.NET Framework 4.8.

*None. No NuGet, no bundled libraries, no icon fonts — every icon is drawn in code,
and the app compiles with the C# compiler that ships with Windows against .NET
Framework 4.8.*
