# רישיונות ורכיבי צד שלישי · Licensing and third-party components

## תקציר · Summary

| מה | רישיון | מה מותר |
|---|---|---|
| **הקוד של Subtext** (`src\*.cs`, `build.cmd`, `build\*.ps1`) | **MIT** | הכול. לקחת, לשנות, למכור, לבנות גרסה למק — בלי לשאול ובלי לשלם |
| **FFmpeg** (מנוע המדיה, מצורף ל‑EXE) | **GPLv3** | להשתמש ולהפיץ, כל עוד מלווים בקוד המקור או בהצעה לקבלו |
| **גופן IBM Plex Sans Hebrew** (מוטמע ב‑EXE) | **SIL OFL 1.1** | להשתמש, להטמיע ולהפיץ; אסור למכור את הגופן בפני עצמו |
| **ידע מ‑Subtitle Edit** (רשימת חריגות בקריאת חותמות זמן) | **MIT** | הכול; שורת זכויות היוצרים נשמרת |
| **מילון Hspell** (בדיקת איות, **לא ב‑EXE**, יורד בנפרד רק למי שמבקש) | **AGPLv3** | להשתמש ולהפיץ, עם הרישיון והפניה למקור |
| **תמלול על המחשב** (בטא; **לא ב‑EXE**, יורד בנפרד רק למי שמבקש): המנוע sherpa-onnx, ‏ONNX Runtime, והמודל העברי zipformer2-66m | **Apache 2.0**, **MIT**, **CC BY 4.0** | להשתמש ולהפיץ, עם הרישיונות והקרדיט ליוצרים |
| **מודלים נוספים על המחשב** (בטא, מ‑0.8.46; **לא ב‑EXE**, כל אחד יורד בנפרד רק למי שמבקש): תרגום קל בשני הכיוונים (quickmt), תרגום מדויק (TranslateGemma), תמלול מדויק (Whisper של ivrit.ai), העוזר על המחשב (Gemma 4), והמנועים שלהם | **CC BY 4.0**, **Gemma Terms of Use**, **Apache 2.0**, **MIT** ועוד - הפירוט למטה | להשתמש ולהפיץ, עם הרישיונות והקרדיט; ב‑Gemma - גם לפי תנאי השימוש שלה |

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

*Subtext uses **FFmpeg**, unmodified, as its media engine. A compressed copy of
`ffmpeg.exe` is embedded in the published EXE and unpacked to the user's folder on first
run. The app runs it as a separate process; it does not link against it or patch it.*

| | |
|---|---|
| בילד · Build | `essentials_build` by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) |
| גרסה · Version | `9.0.2-essentials_build-www.gyan.dev` (מ-0.8.8 · since 0.8.8) |
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

## גופן IBM Plex Sans Hebrew

הממשק מוצג בגופן **IBM Plex Sans Hebrew** של IBM, מוטמע ב‑EXE בשלושה משקלים
(רגיל, חצי‑מודגש, מודגש; ‏`assets\fonts`, כ‑290KB). הגופן נכלל **ללא שינוי ובלי
שינוי שם**. עד גרסה 0.8.0 הממשק היה בגופן Assistant.

*The UI is set in **IBM Plex Sans Hebrew** by IBM, embedded in the EXE in three
weights, unmodified and unrenamed. Up to 0.8.0 the UI used Assistant.*

| | |
|---|---|
| רישיון · License | **SIL Open Font License 1.1** |
| נוסח הרישיון · License text | [`licenses/OFL-1.1-IBM-Plex.txt`](licenses/OFL-1.1-IBM-Plex.txt) · וגם [`assets/fonts/OFL.txt`](assets/fonts/OFL.txt) |
| מקור · Source | https://github.com/IBM/plex · https://fonts.google.com/specimen/IBM+Plex+Sans+Hebrew |

‏`Plex` הוא **שם גופן שמור** (‏Reserved Font Name) — מי שיוצר גרסה נגזרת חייב
לתת לה שם אחר.

---

## מילון Hspell — בדיקת איות

בדיקת האיות נשענת על המילון העברי של פרויקט **Hspell** מאת נדב הראל ודן קנייגסברג,
בגרסה 1.4, בפורמט Hunspell (‏`he_IL.aff`, ‏`he_IL.dic`).

**המילון לא נמצא בתוך ה‑EXE.** הוא יורד בנפרד, פעם אחת, רק כשהמשתמש מבקש
(כ‑1.2MB דחוס), ונשמר בתיקייה משלו: ‏`%LOCALAPPDATA%\SubtitleStudio\dict`,
או `dict` ליד ה‑EXE בגרסה הניידת. הקבצים מופצים **ללא שינוי**, והתוכנה בודקת
את טביעת ה‑SHA‑256 שלהם לפני שהיא משתמשת בהם.

המנוע שקורא את המילון (`src\SpellEngine.cs`) נכתב מאפס ל‑C# 5, לא הועתק
משום ספרייה, והוא MIT כמו שאר הקוד. הוא **קורא** את קובצי המילון, ולא מקשר אליהם קוד.
גם רשימת מילות הלימוד (`assets\spell\torah-he.txt`, ארמית, שמות וראשי תיבות
שחסרים ב‑Hspell) היא שלנו, ב‑MIT.

*Spell checking uses the Hebrew dictionary of the **Hspell** project by Nadav Har'El and
Dan Kenigsberg, version 1.4, in Hunspell format. **The dictionary is not in the EXE.** It is
downloaded separately, once, only when the user asks for it, and is distributed unmodified
(its SHA‑256 is verified before use). The engine that reads it (`src\SpellEngine.cs`) is an
independent C# 5 implementation under MIT, as is our Torah‑study word list.*

| | |
|---|---|
| גרסה · Version | Hspell 1.4 (‏`he_IL` Hunspell) |
| זכויות יוצרים · Copyright | 2000‑2017 Nadav Har'El, Dan Kenigsberg |
| רישיון · License | **GNU Affero General Public License v3** |
| נוסח הרישיון · License text | [`licenses/AGPL-3.0-Hspell.txt`](licenses/AGPL-3.0-Hspell.txt) |
| מקור · Source | http://hspell.ivrix.org.il/ |

---

## תמלול על המחשב (בטא) — מנוע ומודל שיורדים בנפרד

מגרסה 0.8.45 התוכנה יודעת לתמלל עברית על המחשב, בלי אינטרנט. בשביל זה היא מורידה,
**פעם אחת ורק כשהמשתמש מבקש**, חבילה של כ‑62MB ממהדורה ייעודית במאגר הזה
(`local-stt-he-1`). **שום דבר ממנה לא נמצא בתוך ה‑EXE.** היא נשמרת בתיקייה משלה:
‏`%LOCALAPPDATA%\SubtitleStudio\models\stt-he`, או `models\stt-he` ליד ה‑EXE בגרסה
הניידת, והתוכנה בודקת את טביעת ה‑SHA‑256 של כל קובץ לפני שהיא משתמשת בו.

התוכנה **מריצה** את המנוע כתהליך נפרד (כמו את FFmpeg) ולא מקשרת אליו קוד. הקוד
שלנו סביבו (`src\LocalStt.cs`) הוא MIT כמו שאר הקוד.

*From 0.8.45 Subtext can transcribe Hebrew on the PC, offline (beta). For that it downloads,
once and only when the user asks, a pack of about 62 MB from a dedicated release in this
repository (`local-stt-he-1`). Nothing of it is inside the EXE. Subtext runs the engine as a
separate process; our own code around it is MIT. Every file's SHA‑256 is verified before use,
and the license texts travel with the pack (`LICENSES.txt`).*

| רכיב · Component | רישיון · License | מקור · Source |
|---|---|---|
| **sherpa-onnx** 2.0.1 (`sherpa-onnx.exe`), ללא שינוי · unmodified | Apache License 2.0 | https://github.com/k2-fsa/sherpa-onnx |
| **ONNX Runtime** 1.28.2 (`onnxruntime.dll`), ללא שינוי · unmodified | MIT, © Microsoft Corporation | https://github.com/microsoft/onnxruntime |
| **zipformer2-66m-streaming-he** — מודל זיהוי דיבור עברי מאת **יעקב קולני** (Yakov Kolani), על הקלטות של **ivrit.ai**. הקבצים ללא שינוי; ‏`bpe.vocab` הוא אוצר החלקים של המודל עצמו, שיוצא מ‑`bpe.model` שלו · a Hebrew speech model by Yakov Kolani, trained on ivrit.ai audio; files unmodified, `bpe.vocab` exported from the model's own `bpe.model` | Creative Commons Attribution 4.0 (CC BY 4.0) | https://huggingface.co/thewh1teagle/zipformer2-66m-streaming-he · https://huggingface.co/ivrit-ai/zipformer2-66m-streaming-he |

נוסח הרישיונות של המנוע ושל ONNX Runtime נמצא בקובץ `LICENSES.txt` שיורד עם החבילה
ונשמר ליד הקבצים. החבילה נארזת בסקריפט `build\make-local-stt.ps1`.
| ההפצה שלנו · Our copy | המהדורה [`spell-he-1.4`](https://github.com/BeniaBot/subtitle-studio/releases/tag/spell-he-1.4), עם הרישיון וקובץ ה‑README המקורי |

---

## מודלים נוספים על המחשב (בטא, מ‑0.8.46) — תרגום ותמלול מדויק

מגרסה 0.8.46 החלון ״מודלים על המחשב״ מציע עוד שלושה מודלים. כל אחד יורד **פעם אחת ורק
כשהמשתמש בוחר בו**, ממהדורה ייעודית במאגר הזה (טרום הפצה), לתיקייה משלו תחת
‏`%LOCALAPPDATA%\SubtitleStudio\models` (או `models` ליד ה‑EXE בגרסה הניידת). **שום דבר
מהם לא נמצא בתוך ה‑EXE.** התוכנה בודקת את טביעת ה‑SHA‑256 של כל קובץ, ומריצה את המנועים
כתהליך נפרד - בלי לקשר אליהם קוד. נוסח כל הרישיונות יורד עם כל חבילה (`LICENSES.txt`)
ונשמר ליד הקבצים. מחיקה בחלון מוחקת את התיקייה כולה.

*From 0.8.46 the "Models on this PC" window offers three more models. Each one is downloaded
once, only when the user picks it, from a dedicated pre-release in this repository, into its own
folder. Nothing of it is inside the EXE. Every file's SHA‑256 is verified, the engines run as
separate processes, and each pack carries the full license texts (`LICENSES.txt`).*

**תרגום קל, מאנגלית לעברית** — המהדורה [`local-mt-1`](https://github.com/BeniaBot/subtitle-studio/releases/tag/local-mt-1)

| רכיב · Component | רישיון · License | מקור · Source |
|---|---|---|
| **quickmt-en-he** — מודל תרגום של פרויקט quickmt. ‏`model.bin` נשמר מחדש במשקלים של 8 ביט (אותו כימות ש‑CTranslate2 עושה בטעינה; התרגום זהה), השאר ללא שינוי · weights re-saved as int8, the rest unmodified | Creative Commons Attribution 4.0 (CC BY 4.0) | https://huggingface.co/quickmt/quickmt-en-he |
| **Edge-Punct-Casing** — מודל פיסוק לאנגלית מאת frankyoujian, כפי שנארז ב‑sherpa-onnx, ללא שינוי · unmodified | Apache License 2.0 | https://huggingface.co/frankyoujian/Edge-Punct-Casing |
| **Python** 3.12.10 (החבילה הרשמית להטמעה, בלי כמה מודולים שלא נטענים) · the official embeddable package | Python Software Foundation License | https://www.python.org |
| **CTranslate2** 4.8.3, עם ה‑OpenMP של Intel שמגיע איתו · with the bundled Intel OpenMP runtime | MIT; Intel Simplified Software License | https://github.com/OpenNMT/CTranslate2 |
| **SentencePiece** 0.2.2 | Apache License 2.0 | https://github.com/google/sentencepiece |
| **sherpa-onnx** 2.0.1 (המודול לפייתון) ו‑**ONNX Runtime** · Python module and ONNX Runtime | Apache License 2.0; MIT | https://github.com/k2-fsa/sherpa-onnx |
| **Microsoft Visual C++ runtime** 14.44 — שלושה קבצים מהמתקין הרשמי, ללא שינוי, כדי שהמנוע ירוץ בלי להתקין דבר · three files from the official redistributable, unmodified | Microsoft Visual Studio redistributable files | https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist |

**תרגום מדויק, בין שפות רבות** — המהדורה [`local-llm-1`](https://github.com/BeniaBot/subtitle-studio/releases/tag/local-llm-1)

| רכיב · Component | רישיון · License | מקור · Source |
|---|---|---|
| **TranslateGemma 4B** של Google, בכימות Q4_K_M (GGUF) מאת mradermacher, ללא שינוי; יורד בחלקים ומתחבר במחשב · unmodified, downloaded in parts and joined | **Gemma Terms of Use** | https://huggingface.co/google/translategemma-4b-it |
| **llama.cpp**, בנייה 12074 (`llama-server.exe` והספריות שלו) — המהדורה הרשמית לווינדוס, ללא שינוי · the official Windows CPU build, unmodified | MIT | https://github.com/ggml-org/llama.cpp |
| **LLVM OpenMP** (`libomp.dll`), כפי שמגיע עם llama.cpp · as shipped with it | Apache License 2.0 with LLVM Exceptions | https://github.com/llvm/llvm-project |
| **Microsoft Visual C++ runtime** 14.44 (כמו למעלה · as above) | Microsoft Visual Studio redistributable files | — |

Gemma is provided under and subject to the Gemma Terms of Use found at
[ai.google.dev/gemma/terms](https://ai.google.dev/gemma/terms), and its use is subject to the
[Gemma Prohibited Use Policy](https://ai.google.dev/gemma/prohibited_use_policy).
מי שמוריד את המודל מתחייב להשתמש בו לפי התנאים והמדיניות האלה; בחלון ההורדה ובחלון ״מודלים על המחשב״ כתוב שהוא כפוף להם.

**תמלול עברית מדויק** — המהדורה [`local-stt-he-2`](https://github.com/BeniaBot/subtitle-studio/releases/tag/local-stt-he-2) (רץ עם הפייתון ו‑CTranslate2 של התרגום הקל, שיורדים איתו)

| רכיב · Component | רישיון · License | מקור · Source |
|---|---|---|
| **whisper-large-v3-turbo-ct2** של **ivrit.ai** — Whisper large-v3-turbo של OpenAI (MIT) שאומן לעברית. ‏`model.bin` נשמר במשקלים של 8 ביט (כמו ש‑CTranslate2 עושה בטעינה; התמלול זהה בבית לבית), השאר ללא שינוי · weights re-saved as int8, the rest unmodified | Apache License 2.0 | https://huggingface.co/ivrit-ai/whisper-large-v3-turbo-ct2 |
| **faster-whisper** 1.2.1 (בלי מודל ה‑VAD שלא בשימוש · without the unused VAD model) | MIT | https://github.com/SYSTRAN/faster-whisper |
| **NumPy** 2.5.5, עם הספריות שהוא מביא (OpenBLAS ועוד) · with its bundled libraries | BSD 3-Clause, והרישיונות שלהן · and theirs | https://numpy.org |
| **Tokenizers** 0.23.3 | Apache License 2.0 | https://github.com/huggingface/tokenizers |
| **tqdm** 4.70.1, ללא שינוי · unmodified | MPL-2.0 AND MIT | https://github.com/tqdm/tqdm |

**תרגום קל מעברית לאנגלית** (מ‑0.8.48) — המהדורה [`local-mt-2`](https://github.com/BeniaBot/subtitle-studio/releases/tag/local-mt-2) (רץ עם המנוע של התרגום הקל, מ‑`local-mt-1`)

| רכיב · Component | רישיון · License | מקור · Source |
|---|---|---|
| **quickmt-he-en** — מודל תרגום של פרויקט quickmt. ‏`model.bin` נשמר מחדש במשקלים של 8 ביט (התרגום זהה), השאר ללא שינוי · weights re-saved as int8, the rest unmodified | Creative Commons Attribution 4.0 (CC BY 4.0) | https://huggingface.co/quickmt/quickmt-he-en |

**העוזר החכם על המחשב** (מ‑0.8.48) — המהדורה [`local-assist-1`](https://github.com/BeniaBot/subtitle-studio/releases/tag/local-assist-1) (רץ עם llama.cpp, מ‑`local-llm-1`)

| רכיב · Component | רישיון · License | מקור · Source |
|---|---|---|
| **Gemma 4 E2B** של Google — ההמרה ל‑GGUF של ggml-org (Q4_0), ללא שינוי, וראש הניחוש שלה (MTP) · the ggml-org GGUF conversion, unmodified, and its multi-token-prediction head | Apache License 2.0 | https://huggingface.co/google/gemma-4-E2B-it · https://huggingface.co/ggml-org/gemma-4-E2B-it-GGUF |

הקוד שלנו סביב כל אלה (`src\LocalCatalog.cs`, ‏`LocalCt2.cs`, ‏`LocalLlm.cs`, ‏`LocalTranslate.cs`,
‏`build\local\*.py`) הוא MIT כמו שאר הקוד. החבילות נארזות בסקריפט `build\make-model-pack.ps1`.

---

## רכיבים נוספים · Everything else

אין ספריות מצורפות. אין NuGet ואין גופני אייקונים — כל האייקונים מצוירים בקוד,
והתוכנה מהודרת עם מהדר ה‑C# המובנה בווינדוס מול ‎.NET Framework 4.8.

**מפענח LZMA** (`src\Lzma.cs`, מ-0.8.8): העברה של המפענח מ-[LZMA SDK](https://www.7-zip.org/sdk.html)
של איגור פבלוב, שהוא **נחלת הכלל** (public domain). מנוע הווידאו נארז בתוך ה-EXE ב-LZMA —
27 מגה במקום 37. הקוד שלנו סביבו ב-MIT.

*No bundled libraries. No NuGet, no icon fonts — every icon is drawn in code, and the app
compiles with the C# compiler that ships with Windows against .NET Framework 4.8.*

***LZMA decoder** (`src\Lzma.cs`, since 0.8.8): a port of the decoder from Igor Pavlov's
[LZMA SDK](https://www.7-zip.org/sdk.html), which is in the **public domain**. The video engine
is packed inside the EXE with LZMA — 27 MB instead of 37.*

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
