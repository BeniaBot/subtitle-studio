# test-import.ps1 - טקסט לכתוביות (0.8.9), ומהירות הניגון. כל מקרה נולד ממדידה ב-text-sweep.ps1 על טקסטים
# אמיתיים ומקרי קצה: פסקה ענקית, שורה ריקה אחת, פסוקים ושעות ביום, SRT שהודבק, טקסט מ-PDF, Markdown.
# צפוי: 29 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }

$fmt = T 'Formats'
$optT = $asm.GetType('SubtitleStudio.Formats+TextImportOptions')
$imp3 = $fmt.GetMethods($SF) | Where-Object { $_.Name -eq 'ImportPlainText' -and $_.GetParameters().Count -eq 3 } | Select-Object -First 1
function Imp([string]$text, [hashtable]$set) {
    $o = [Activator]::CreateInstance($optT)
    $o.Auto = $true
    if ($set) { foreach ($k in $set.Keys) { $o.$k = $set[$k] } }
    $a = New-Object object[] 3; $a[0] = $text; $a[1] = $o; $a[2] = $null
    $cues = @($imp3.Invoke($null, $a))
    return @{ Cues = $cues; Info = $a[2] }
}
function Lines($c) { return @(([string]$c.Text) -split "`n") }
function AllFit($cues) {
    foreach ($c in $cues) { $ls = Lines $c; if ($ls.Count -gt 2) { return $false }; foreach ($l in $ls) { if ($l.Length -gt 42) { return $false } } }
    return $true
}
function MaxCps($cues) {
    $m = 0
    foreach ($c in $cues) { $ch = (([string]$c.Text) -replace '\s', '').Length; $s = [Math]::Max(0.001, ($c.End - $c.Start) / 1000.0); if ($ch / $s -gt $m) { $m = $ch / $s } }
    return $m
}
$nl = "`n"

Write-Host 'חלוקה'
$ten = (1..10 | ForEach-Object { "שורה מספר $_ של כתוביות" })
$r = Imp (($ten[0..4] -join $nl) + $nl + $nl + ($ten[5..9] -join $nl))
Check 'עשר שורות עם שורה ריקה אחת באמצע - עשר כתוביות (עד 0.8.9: שתיים)' ($r.Cues.Count -eq 10) ("cues=" + $r.Cues.Count)

$para = 'בשיעור הקודם דיברנו על היסודות, והיום אנחנו ממשיכים צעד אחד קדימה. חשוב להבין שכל מה שנלמד כאן נשען על מה שכבר ראינו, ולכן כדאי לחזור על החומר לפני שממשיכים. מי שלא היה בשיעור הקודם יכול למצוא את ההקלטה באתר, ושם יש גם סיכום קצר של כל הנקודות החשובות.'
$r = Imp $para
Check 'פסקה ארוכה - כמה כתוביות, כל אחת עד שתי שורות של 42' ($r.Cues.Count -ge 4 -and (AllFit $r.Cues)) ("cues=" + $r.Cues.Count)
Check 'ואפשר לקרוא אותן (עד 15 תווים בשנייה)' ((MaxCps $r.Cues) -le 15.5) ("max=" + [int](MaxCps $r.Cues))
$ends = @($r.Cues | Where-Object { ([string]$_.Text).TrimEnd() -match '[.,?!]$' }).Count
Check 'החיתוכים בסוף משפט או בפסיק' ($ends -eq $r.Cues.Count) ("$ends of " + $r.Cues.Count)
Check 'לא נאבדה אף מילה' ((($r.Cues | ForEach-Object { ([string]$_.Text) -replace "`n", ' ' }) -join ' ') -eq $para) ''
Check 'פיצול מדווח בתצוגה המקדימה' ($r.Info.SplitUnits -ge 1) ("split=" + $r.Info.SplitUnits)

$r = Imp ("שלום לכולם" + $nl + "וברוכים הבאים" + $nl + $nl + "היום נלמד על כתוביות")
Check 'קטע של שתי שורות קצרות - כתובית אחת עם השבירה שהוקלדה' ($r.Cues.Count -eq 2 -and ([string]$r.Cues[0].Text) -eq ("שלום לכולם" + $nl + "וברוכים הבאים")) ([string]$r.Cues[0].Text -replace "`n", ' / ')

$r = Imp ("- שלום, מה שלומך?" + $nl + "- ברוך השם, ואתה?")
Check 'דיאלוג: ״- ״ בתחילת השורות נשאר' ($r.Cues.Count -eq 1 -and ([string]$r.Cues[0].Text).StartsWith('- ')) ([string]$r.Cues[0].Text -replace "`n", ' / ')

$r = Imp ("למה דווקא כאן?" + $nl + "כניסה אטרקטיבית לשוק עם אפשרויות רכישה במחירים טובים ביחס לפוטנציאל של האזור כולו, לאורך זמן.")
Check 'כותרת בתחילת פסקה - כתובית משלה' ([string]$r.Cues[0].Text -eq 'למה דווקא כאן?') ([string]$r.Cues[0].Text)

$wrapped = "זהו טקסט שהועתק ממסמך ונשבר לשורות באמצע המשפטים, כמו שקורה בדואר`nאלקטרוני ובמסמכים ישנים. התוכנה צריכה לחבר את השורות בחזרה למשפטים`nשלמים ורק אחר כך לחלק אותם לכתוביות."
$r = Imp $wrapped
$joined = ($r.Cues | ForEach-Object { ([string]$_.Text) -replace "`n", ' ' }) -join ' | '
Check 'פסקה שנשברה בעימוד - ״בדואר אלקטרוני״ באותה כתובית' ($joined -match 'בדואר אלקטרוני') $joined

$r = Imp 'הזדמנות ייחודית לפנטהאוזים — הזדמנות יוצאת דופן ליהנות מנכס יוקרתי בתנאי רכישה אטרקטיביים במיוחד לכל המשפחה'
$bad = @($r.Cues | Where-Object { ([string]$_.Text).StartsWith([string][char]0x2014) }).Count
Check 'מקף מפריד לא פותח כתובית' ($bad -eq 0) (($r.Cues | ForEach-Object { [string]$_.Text -replace "`n", ' / ' }) -join ' | ')

Write-Host 'זמנים בתחילת שורה'
$r = Imp ("0:00 פתיחה" + $nl + "0:45 למה צריך כתוביות" + $nl + "2:10 כתיבת הטקסט")
Check 'פרקים של יוטיוב: הזמנים משמשים' ($r.Info.StampsUsed -eq 3 -and $r.Cues[1].Start -eq 45000 -and ([string]$r.Cues[1].Text) -eq 'למה צריך כתוביות') ("start=" + $r.Cues[1].Start)
$r = Imp ("1:1 בראשית ברא אלהים את השמים ואת הארץ" + $nl + "1:2 והארץ היתה תהו ובהו וחשך על פני תהום" + $nl + "1:3 ויאמר אלהים יהי אור ויהי אור")
Check 'פסוקים ״1:1״ - לא חותמות (צפוף מכדי לקרוא), והמספר נשאר בטקסט' ($r.Info.StampsUsed -eq 0 -and ([string]$r.Cues[0].Text).StartsWith('1:1')) ("used=" + $r.Info.StampsUsed)
$clock = "7:15 יצאנו מהבית" + $nl + "10:30 הגענו לחניון" + $nl + "13:00 עצרנו לארוחת צהריים" + $nl + "16:45 התחלנו לחזור"
$r = Imp $clock @{ MediaMs = [long]180000 }
Check 'שעות ביום בסרטון של שלוש דקות - לא חותמות' ($r.Info.StampsUsed -eq 0) ("used=" + $r.Info.StampsUsed)
$r = Imp ("[00:00:01] שלום לכולם" + $nl + "[00:00:04] היום נלמד על כתוביות" + $nl + "[00:00:08] זה פשוט מאוד")
Check 'תמלול עם זמנים בסוגריים' ($r.Info.StampsUsed -eq 3 -and $r.Cues[2].Start -eq 8000) ("start=" + $r.Cues[2].Start)
$r = Imp ("0:05 " + $para + $nl + "0:20 וזה הסוף")
$inside = @($r.Cues | Where-Object { $_.End -le 20000 }).Count
Check 'יחידה ארוכה עם חותמת: הכתוביות שלה נכנסות עד החותמת הבאה' ($inside -eq $r.Cues.Count - 1) ("$inside of " + ($r.Cues.Count - 1))

Write-Host 'קובץ כתוביות שהודבק כטקסט'
$r = Imp ("00:00:01.000 --> 00:00:03.500" + $nl + "שלום לכולם" + $nl + $nl + "00:00:04.000-->00:00:06.000" + $nl + "וברוכים הבאים" + $nl + $nl + "00:00:07,000 -- > 00:00:09,000" + $nl + "היום נלמד")
Check 'SRT ״שבור״ - נטען עם הזמנים' ($r.Info.WasSubtitles -and $r.Cues.Count -eq 3 -and $r.Cues[0].Start -eq 1000 -and $r.Cues[2].End -eq 9000) ("cues=" + $r.Cues.Count)
$r = Imp ("1" + $nl + "00:00:01,000 --> 00:00:03,500" + $nl + "שלום" + $nl + $nl + "2" + $nl + "00:00:04,000 --> 00:00:06,000" + $nl + "להתראות")
$junk = @($r.Cues | Where-Object { ([string]$_.Text) -match '-->|^\d+$' }).Count
Check 'SRT רגיל: בלי מספרים וחיצים בכתוביות' ($r.Cues.Count -eq 2 -and $junk -eq 0) ("cues=" + $r.Cues.Count)
$r = Imp ("מאמר רגיל בלי זמנים" + $nl + "00:00:01,000 --> 00:00:02,000" + $nl + "שורה" + $nl + $nl + "ועוד פסקה ארוכה מאוד של טקסט רגיל שאין בה שום זמן" + $nl + "ועוד אחת" + $nl + "ועוד שורה")
Check 'שורת זמן מקרית בתוך מאמר - לא הופכת אותו לקובץ כתוביות' (-not $r.Info.WasSubtitles) ''

Write-Host 'ניקוי'
$pdf = "משקר" + [char]0x200F + "ויש" + [char]0x200F + "לו" + [char]0x200F + "כסף," + [char]0x200F + "או" + [char]0x200F + "ששמע" + [char]0x200F + "לשון" + [char]0x200F + "הרע"
$r = Imp $pdf
Check 'טקסט מ-PDF: תווי הכיוון במקום רווחים הופכים לרווחים' (([string]$r.Cues[0].Text -replace "`n", ' ') -eq 'משקר ויש לו כסף, או ששמע לשון הרע') ([string]$r.Cues[0].Text -replace "`n", ' / ')
$r = Imp ("שנת " + [char]0x200F + "2026 הייתה טובה")
Check 'תו כיוון בודד בטקסט רגיל - נעלם, בלי רווח מיותר' (([string]$r.Cues[0].Text) -eq 'שנת 2026 הייתה טובה') ([string]$r.Cues[0].Text)
$r = Imp ("# הכותרת" + $nl + $nl + "פסקה עם **הדגשה** באמצע")
$all = ($r.Cues | ForEach-Object { [string]$_.Text }) -join ' | '
Check 'Markdown: בלי ״#״ ובלי ״**״' ($all -notmatch '\*\*|#') $all
$r = Imp ("1. שלום לכולם" + $nl + "2. היום נלמד" + $nl + "3. זה פשוט")
Check 'מספור רץ 1. 2. 3. יורד' (([string]$r.Cues[0].Text) -eq 'שלום לכולם') ([string]$r.Cues[0].Text)
$r = Imp ("המסקנה:" + $nl + "1. לבדוק" + $nl + "ועוד דבר")
Check 'מספור חלקי נשאר - הוא חלק מהטקסט' (@($r.Cues | Where-Object { ([string]$_.Text) -eq '1. לבדוק' }).Count -eq 1) (($r.Cues | ForEach-Object { [string]$_.Text }) -join ' | ')

Write-Host 'קצוות'
$r = Imp "   `n`n `t `n"
Check 'טקסט ריק - אפס כתוביות, בלי חריגה' ($r.Cues.Count -eq 0) ''
$r = Imp 'https://www.example.com/some/very/long/path/that/goes/on/and/on/without/any/spaces/at/all/forever'
Check 'מילה ארוכה בלי רווחים - כתובית אחת, בלי לולאה ובלי חריגה' ($r.Cues.Count -eq 1) ''
$hour = (1..150 | ForEach-Object { $para }) -join ($nl + $nl)
$sw = [Diagnostics.Stopwatch]::StartNew(); $r = Imp $hour; $ms = $sw.ElapsedMilliseconds
Check 'שיעור של שעה (150 פסקאות) - קריא כולו, ומהר (התצוגה המקדימה רצה בכל הקלדה)' ((AllFit $r.Cues) -and $ms -lt 1500) ("cues=" + $r.Cues.Count + " ms=$ms")

Write-Host 'מהירות הניגון'
$sp = T 'SpeedPicker'
$step = $sp.GetMethod('StepFrom', $SF); $snap = $sp.GetMethod('Snap', $SF)
function St($v, $d) { return [double]$step.Invoke($null, @([double]$v, [int]$d)) }
Check 'צעד: 0.25 עולה ל-0.3, ומ-1 ל-1.1 (עשיריות שלמות, ודרך 1)' ((St 0.25 1) -eq 0.3 -and (St 1 1) -eq 1.1 -and (St 1.25 1) -eq 1.3 -and (St 1.25 -1) -eq 1.2 -and (St 0.3 -1) -eq 0.25) ("" + (St 0.25 1) + " " + (St 1 1) + " " + (St 1.25 1) + " " + (St 1.25 -1))
Check 'גבולות: לא מתחת ל-0.25 ולא מעל 4; וקרוב לרגילה - רגילה' ((St 4 1) -eq 4 -and (St 0.25 -1) -eq 0.25 -and [double]$snap.Invoke($null, @([double]1.02)) -eq 1 -and [double]$snap.Invoke($null, @([double]9)) -eq 4) ''

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
