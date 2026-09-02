# יומן שינויים · Changelog

## v0.1.0 — בטא ראשונה · First beta

### כתוביות
- כתיבה ועריכה מול פס הקול; גרירה להזזה, משיכת קצה להארכה
- פתיחת `SRT` · `VTT` · `ASS` · `SUB`, או טקסט רגיל שמתחלק לכתוביות לבד
- קבצים ישנים בעברית (Windows‑1255) נפתחים נכון
- שליפת ערוץ כתוביות מסרט קיים
- תיקון סנכרון ויזואלי: לכולן · מכאן והלאה · לאחת בלבד; ומתיחה בין שתי נקודות
- תיקון אוטומטי של חפיפות, משכים קצרים ושורות ארוכות
- גופן, גודל, צבע, מסגרת, רקע ומיקום, עם תצוגה מקדימה

### וידאו
- צריבה בתמונה (עברית נכונה, כולל עברית ואנגלית באותה שורה) או ערוץ כתוביות נפרד
- חיתוך קטע בסימון על הציר, מהיר או מדויק
- עוצמה · נרמול · ניקוי רעש · הפרדת שמע · המרה · סיבוב · חיתוך שוליים · מהירות ·
  עמעום · איחוד · GIF · תמונה מייצגת · גרסה לנייד
- הקטנה לגודל יעד בקידוד דו‑מעברי (״עד 200MB״)
- איכות מקסימלית כברירת מחדל; הקובץ המקורי לא משתנה

### התוכנה
- קובץ EXE אחד: בלי התקנה, בלי אינטרנט, בלי חבילות
- מנוע הווידאו נפרס פעם אחת בהפעלה הראשונה
- מצב נייד (`portable.txt`), ערכה כהה ובהירה, תמיכה ב‑DPI גבוה
- בדיקת עדכון בכל הפעלה ועדכון בלחיצה אחת (ניתן לכיבוי)

דרישות: ווינדוס 10 או 11.

---

### English
Write and time subtitles against a waveform · open SRT/VTT/ASS/SUB or plain text ·
legacy Hebrew encodings handled · extract a subtitle track from a video · fix sync
visually (all cues / from here on / one cue) · auto-fix overlaps and short cues ·
styling with live preview · burn into the picture with correct Hebrew RTL or mux as a
separate track · visual trim · 17 media tools · target-size two-pass compression ·
max quality by default, original never modified. One portable EXE, Windows 10/11.
