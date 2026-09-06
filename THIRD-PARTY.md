# רישיונות ורכיבי צד שלישי · Licensing and third-party components

## תקציר · Summary

| מה | רישיון | מה מותר |
|---|---|---|
| **הקוד של אולפן הכתוביות** (`src\*.cs`, `build.cmd`, `build\*.ps1`) | **MIT** | הכול. לקחת, לשנות, למכור, לבנות גרסה למק — בלי לשאול ובלי לשלם |
| **FFmpeg** (מנוע המדיה, מצורף ל‑EXE) | **GPLv3** | להשתמש ולהפיץ, כל עוד מלווים בקוד המקור או בהצעה לקבלו |
| **גופן Assistant** (מוטמע ב‑EXE) | **SIL OFL 1.1** | להשתמש, להטמיע ולהפיץ; אסור למכור את הגופן בפני עצמו |
| **ידע מ‑Subtitle Edit** (רשימת חריגות בקריאת חותמות זמן) | **MIT** | הכול; שורת זכויות היוצרים נשמרת |

הנוסח המלא של כולם נמצא בתיקייה [`licenses/`](licenses) — **וגם בתוך ה‑EXE עצמו**:
״על התוכנה ← שמירת נוסח הרישיונות לתיקייה״.

*The full text of all of them is in [`licenses/`](licenses), and also **inside the EXE**
(About → save license texts).*

---

## רוצים לקחת את הקוד? · Want to reuse this code?

**קחו.** הקוד שלנו הוא MIT — הרישיון הכי מתירני שיש. אין צורך לבקש רשות,
אין צורך לפרסם את השינויים שלכם, ואין צורך לשמור על אותו רישיון.
מה שכן נדרש: לצרף את שורת זכויות היוצרים והרישיון (`LICENSE`) לעותקים שאתם מפיצים.

**גרסה למק או ללינוקס** היא הדוגמה הכי טובה למה שאפשר לעשות:
המנוע כולו נשען על הרצת `ffmpeg` בשורת פקודה, וכל הלוגיקה שאינה WinForms —
`Model.cs`, `Formats.cs`, `Style.cs`, `Ff.cs` — היא C# רגיל בלי תלות בווינדוס.
מי שיחליף את שכבת ה‑UI יקבל את אותה תוכנה.
שימו לב רק שאם תארזו FFmpeg בתוך המוצר שלכם, החובות של GPLv3 עוברות אליכם (ראו למטה).

*Take it. Our code is MIT: no permission needed, no obligation to publish your changes,
no copyleft. Just keep the copyright line and the license text with copies you distribute.
A macOS or Linux port is the obvious thing to do — the engine shells out to `ffmpeg`, and
everything that isn't WinForms (`Model.cs`, `Formats.cs`, `Style.cs`, `Ff.cs`) is plain,
portable C#. If you bundle FFmpeg yourself, the GPLv3 obligations below become yours.*

---

## FFmpeg

התוכנה משתמשת ב‑**FFmpeg** כמנוע המדיה שלה. הקובץ `ffmpeg.exe` נכלל **ללא שינוי**,
דחוס בתוך ה‑EXE, ונפרס לתיקיית המשתמש בהפעלה הראשונה.
התוכנה מריצה אותו **כתהליך נפרד** בשורת פקודה — היא לא מקשרת אליו קוד ולא משנה אותו.

*Subtitle Studio uses **FFmpeg**, unmodified, as its media engine. A compressed copy of
`ffmpeg.exe` is embedded in the published EXE and unpacked to the user's folder on first
run. The app runs it as a separate process; it does not link against it or patch it.*

| | |
|---|---|
| בילד · Build | `essentials_build` by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) |
| גרסה · Version | `9.0.1-essentials_build-www.gyan.dev` |
| רישיון · License | **GNU General Public License v3** (‏`--enable-gpl --enable-version3`) |
| נוסח הרישיון · License text | [`licenses/GPL-3.0.txt`](licenses/GPL-3.0.txt) |
| קוד מקור · Source | https://ffmpeg.org/download.html · https://git.ffmpeg.org/ffmpeg.git |
| הבילדים · Build scripts | https://github.com/GyanD/codexffmpeg |

### הצעת קוד מקור · Written offer

בהתאם ל‑GPLv3 סעיף 6: קוד המקור המלא של בילד ה‑FFmpeg המצורף זמין בקישורים
שלמעלה, ללא תשלום. מי שמעדיף לקבלו בדרך אחרת מוזמן לפתוח
[Issue](https://github.com/BeniaBot/subtitle-studio/issues) והוא יסופק.
ההצעה תקפה לשלוש שנים מיום ההפצה של כל מהדורה.

*Per GPLv3 §6: the complete corresponding source of the bundled FFmpeg build is available
at the links above at no charge. If you prefer another delivery method, open an
[Issue](https://github.com/BeniaBot/subtitle-studio/issues) and it will be provided.
This offer is valid for three years from the distribution of each release.*

### מה זה אומר בפועל · What this means

- **קוד התוכנה הזאת** הוא MIT, ונשאר MIT. הוא לא הופך ל‑GPL בגלל ש‑FFmpeg יושב לידו.
- ה‑EXE שבמהדורות הוא **צירוף** של שתי תוכנות עצמאיות: התוכנה שלנו,
  ובינארי FFmpeg שרץ כתהליך נפרד. אנחנו מפיצים בינארי GPLv3, ולכן ההפצה שלנו
  מלווה בכל מה שסעיף 6 דורש — נוסח הרישיון, פרטי הבילד המדויקים, וקישור לקוד המקור.
- **גם אתם רשאים להפיץ את ה‑EXE הזה הלאה**, כל עוד תעבירו איתו את הדף הזה
  (או את המהדורה המקורית, שכוללת אותו).
- מי שבונה מהמקור בלי `tools\ffmpeg.exe` מקבל תוכנה MIT נקייה, בלי שום רכיב GPL.

---

## Subtitle Edit — ידע, לא קוד

[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit) מאת Nikolaj Olsson,
ברישיון MIT, הוא עורך הכתוביות בקוד פתוח הוותיק בתחום. **לא הועתק ממנו קוד.**
מה שכן נלקח הוא **רשימת החריגות** שקובצי כתוביות מהעולם האמיתי מכילים —
הדרכים השונות שבהן נכתב החץ ב‑SRT, והפרדת זמנים בנקודות. עובדות על מבנה
קובץ אינן מוגנות בזכויות יוצרים, והמימוש ב‑`src\Formats.cs` נכתב מחדש
ל‑C# 5. הקרדיט כאן ניתן מרצון, כי זה הוגן וכי זה מסביר למי שיקרא את הקוד
מאיפה הגיעה רשימת המקרים המוזרים.

הנוסח המלא: [`licenses/MIT-SubtitleEdit.txt`](licenses/MIT-SubtitleEdit.txt).

*No code was copied from Subtitle Edit. What was taken is the **list of real‑world
edge cases** its parser handles — facts about file formats, which are not
copyrightable. The implementation in `src\Formats.cs` is an independent C# 5
rewrite. Credit is given voluntarily.*

---

## גופן Assistant

הממשק מוצג בגופן **Assistant** מאת Ben Nathan, מוטמע ב‑EXE בשלושה משקלים
(‏`assets\fonts`, כ‑223KB). הגופן נכלל **ללא שינוי ובלי שינוי שם**.

*The UI is set in **Assistant** by Ben Nathan, embedded in the EXE in three weights,
unmodified and unrenamed.*

| | |
|---|---|
| רישיון · License | **SIL Open Font License 1.1** |
| נוסח הרישיון · License text | [`licenses/OFL-1.1-Assistant.txt`](licenses/OFL-1.1-Assistant.txt) · וגם [`assets/fonts/OFL.txt`](assets/fonts/OFL.txt) |
| מקור · Source | https://github.com/hafontia/Assistant · https://fonts.google.com/specimen/Assistant |

Assistant מבוסס על Source Sans Pro של Adobe, ולכן `Source` הוא **שם גופן שמור**
(‏Reserved Font Name) — מי שיוצר גרסה נגזרת חייב לתת לה שם אחר.

---

## רכיבים נוספים · Everything else

אין. אין NuGet, אין ספריות מצורפות, אין גופני אייקונים — כל האייקונים מצוירים בקוד,
והתוכנה מהודרת עם מהדר ה‑C# המובנה בווינדוס מול ‎.NET Framework 4.8.

*None. No NuGet, no bundled libraries, no icon fonts — every icon is drawn in code, and
the app compiles with the C# compiler that ships with Windows against .NET Framework 4.8.*

---

## בנייה בלי הבינארי · Building without the binary

הקובץ `tools\ffmpeg.exe` לא נשמר במאגר (הוא גדול מדי, והוא לא הקוד שלנו).
להורדה: [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) → `ffmpeg-release-essentials.7z`
→ להעתיק את `bin\ffmpeg.exe` אל `tools\`.

**הבילד חייב לכלול `libass` + `libfribidi` + `libharfbuzz`** — בלעדיהם צריבת עברית
תצא הפוכה או ריקה. הבילדים של gyan.dev, גם `essentials` וגם `full`, כוללים את שלושתם.
לבדיקה: `ffmpeg -hide_banner -version` וחיפוש `--enable-libfribidi` בשורת ה‑configuration.

*`tools\ffmpeg.exe` is not stored in the repository. Download a gyan.dev build and copy
`bin\ffmpeg.exe` into `tools\`. The build **must** include `libass`, `libfribidi` and
`libharfbuzz`, or Hebrew burn-in comes out reversed — both the `essentials` and `full`
gyan.dev builds do. Verify with `ffmpeg -version`.*

בלי הקובץ הזה הבנייה עדיין מצליחה, ומתקבל EXE קטן שידע לעבוד מול `ffmpeg`
שמותקן במערכת — ובלי שום רכיב GPL בתוכו.

*Without it the build still succeeds and produces a small EXE that uses a system-installed
`ffmpeg` — and contains no GPL component at all.*
