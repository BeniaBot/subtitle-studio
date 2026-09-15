# בדיקת שגיאות (Qa): הכללים, התיקון האוטומטי, והממשק שמחליף את ״תיקון תזמונים אוטומטי״.
#
# **מה חשוב כאן:**
# - הגבולות בדיוק: 20 תווים בשנייה לא מסומן ו-20.8 כן, 42 תווים בשורה לא ו-43 כן.
#   הספים חייבים להיות אותם ספים שהרשימה צובעת בהם, אחרת התג והצבע סותרים.
# - התיקון לא יוצר בעיה חדשה בדרך: הארכה רק לתוך המקום הפנוי, וסידור שורות
#   לא נוגע בדו-שיח.
# - **התיקון יציב:** הרצה שנייה לא משנה כלום. נבדק על 300 מסמכים אקראיים.
# - ההסבר בשורת המצב לא דורס הודעה שמישהו אחר כתב.
# צפוי: 60 בדיקות.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\SubtitleStudio.exe'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IF = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $v = $args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i] = $v }; return ,$a }

$qaT = T 'Qa'; $docT = T 'Doc'; $cueT = T 'Cue'; $kindT = T 'IssueKind'; $formT = T 'MainForm'
function QaCall($name) { $m = $null; foreach ($x in $qaT.GetMethods($SF)) { if ($x.Name -eq $name) { $m = $x } }; return $m }
$mFind = $qaT.GetMethod('Find', $SF); $mFor = $qaT.GetMethod('For', $SF); $mFix = $qaT.GetMethod('FixAll', $SF)
function K($name) { return [Enum]::Parse($kindT, $name) }
function NewDoc($rows) {
    $d = [Activator]::CreateInstance($docT)
    foreach ($r in $rows) {
        $c = $cueT.GetConstructor([Type[]]@([long], [long], [string])).Invoke(@([long]$r[0], [long]$r[1], [string]$r[2]))
        if ($r.Count -gt 3 -and $r[3]) { $c.Untimed = $true }
        $d.Cues.Add($c)
    }
    return $d
}
function Kinds($doc, $i) { return @(@($mFor.Invoke($null, (Pack $doc ([int]$i)))) | ForEach-Object { [string]$_.Kind }) }
function Has($doc, $i, $k) { return (Kinds $doc $i) -contains $k }
function Find($doc) { return @($mFind.Invoke($null, (Pack $doc))) }
function Clean([string]$s) { return -not ([regex]::Replace($s, "‪[^‬]*‬", '') -match '[A-Za-z]') }
function Chars($n) { return ('א' * $n) }

# ================= 1. הכללים =================
Write-Host 'הכללים'
$d = NewDoc @(@(0, 3000, 'שלום לכולם'), @(2500, 5000, 'והיום נלמד'), @(6000, 8000, 'בדיוק צמודה'), @(8000, 10000, 'אחריה'))
Check 'חפיפה: מסומנת, וחמורה' ((Has $d 0 'Overlap') -and (@($mFor.Invoke($null, (Pack $d ([int]0))))[0].Severe)) ((Kinds $d 0) -join ',')
Check 'כתוביות צמודות בדיוק (סוף = התחלה) אינן חפיפה' (-not (Has $d 2 'Overlap')) ''

$d = NewDoc @(@(0, 1000, (Chars 20)), @(2000, 3000, (Chars 21)), @(4000, 5500, (Chars 40)))
Check 'קצב 20 תווים בשנייה בדיוק: לא מסומן' (-not (Has $d 0 'TooFast')) ''
Check 'קצב 21: מסומן, לא חמור' ((Has $d 1 'TooFast') -and -not (@($mFor.Invoke($null, (Pack $d ([int]1))))[0].Severe)) ''
Check 'קצב 26.7: חמור' ((@($mFor.Invoke($null, (Pack $d ([int]2)))) | Where-Object { $_.Severe }).Count -eq 1) ''
Check 'רווחים לא נספרים בקצב (כמו הצבע ברשימה)' (-not (Has (NewDoc @(,@(0, 1000, ('א ' * 20)))) 0 'TooFast')) ''

$d = NewDoc @(@(0, 799, 'קצר'), @(1000, 1800, 'בדיוק'), @(3000, 10001, 'ארוכה'), @(11000, 18000, 'בדיוק שבע'))
Check 'משך 799: קצרה מדי' (Has $d 0 'TooShort') ''
Check 'משך 800: תקין' (-not (Has $d 1 'TooShort')) ''
Check 'משך 7001: ארוכה מדי' (Has $d 2 'TooLong') ''
Check 'משך 7000: תקין' (-not (Has $d 3 'TooLong')) ''

$d = NewDoc @(@(0, 5000, (Chars 42)), @(6000, 11000, (Chars 43)), @(12000, 16000, "א`nב`nג"), @(17000, 21000, "א`n`n   `nב"))
Check 'שורה של 42: תקינה' (-not (Has $d 0 'LongLine')) ''
Check 'שורה של 43: ארוכה' (Has $d 1 'LongLine') ''
Check 'שלוש שורות: מסומן' (Has $d 2 'ManyLines') ''
Check 'שורות ריקות באמצע לא נספרות' (-not (Has $d 3 'ManyLines')) ''

$d = NewDoc @(@(0, 100, '   '), @(1000, 1100, (Chars 60), $true), @(1050, 3000, 'אחרת', $true))
Check 'כתובית ריקה: רק ״ריקה״, בלי קצב או משך' (((Kinds $d 0) -join ',') -eq 'Empty') ((Kinds $d 0) -join ',')
Check 'שורה בלי תזמון: בלי חפיפה, קצב ומשך' (-not ((Kinds $d 1) | Where-Object { $_ -in @('Overlap', 'TooFast', 'TooShort') })) ((Kinds $d 1) -join ',')
Check 'אבל שורה ארוכה מסומנת גם בלי תזמון' (Has $d 1 'LongLine') ''

# ---- מילים ----
$mTitle = $qaT.GetMethod('Title', $SF); $mWhy = $qaT.GetMethod('Why', $SF); $mExplain = $qaT.GetMethod('Explain', $SF)
$okWords = $true; $bad = @()
$sample = NewDoc @(@(0, 300, (Chars 50)), @(200, 9000, "א`nב`nג"), @(9500, 9600, ' '))
foreach ($x in (Find $sample)) { $e = [string]$mExplain.Invoke($null, (Pack $x)); if (-not (Clean $e) -or $e.Length -lt 10) { $okWords = $false; $bad += $e } }
foreach ($k in [Enum]::GetValues($kindT)) {
    foreach ($n in 1, 3) { $t = [string]$mTitle.Invoke($null, (Pack $k ([int]$n))); if (-not (Clean $t)) { $okWords = $false; $bad += $t } }
    $w = [string]$mWhy.Invoke($null, (Pack $k)); if (-not (Clean $w) -or $w.Length -lt 5) { $okWords = $false; $bad += $w }
}
Check 'כל הניסוחים בעברית, בלי אנגלית מחוץ ל-Ltr' $okWords ($bad -join ' | ')
Check 'יחיד: ״כתובית אחת…״, רבים: עם מספר' ((([string]$mTitle.Invoke($null, (Pack (K 'Overlap') ([int]1)))).StartsWith('כתובית אחת')) -and (([string]$mTitle.Invoke($null, (Pack (K 'Overlap') ([int]3)))) -match '3')) ''
Check 'הספים זהים לצבעים ברשימה (20 ו-25)' ($qaT.GetField('FastCps', $SF).GetValue($null) -eq 20 -and $qaT.GetField('SevereCps', $SF).GetValue($null) -eq 25) ''

# ================= 2. תיקון אוטומטי =================
Write-Host 'תיקון אוטומטי'
$dialog = "- " + (Chars 45) + "`n- " + (Chars 10)
$rows = @(
    @(0, 3000, 'חופפת'),                     # 0  חופפת ל-1
    @(2500, 5000, 'שנייה'),                  # 1
    @(6000, 6400, 'קצרה עם מקום'),           # 2  ואחריה רווח גדול
    @(9000, 9400, 'קצרה בלי מקום'),          # 3  הבאה מתחילה מיד
    @(9450, 12000, 'צמודה'),                 # 4
    @(13000, 14000, (Chars 30)),             # 5  מהירה, יש מקום עד 20000
    @(20000, 21000, ('שורה ארוכה מאוד שהיא יותר מארבעים ושתיים תווים בוודאות גמורה')),   # 6  63 תווים
    @(22000, 27000, $dialog),                # 7  דו-שיח עם שורה ארוכה
    @(28000, 34000, ((Chars 50) + ' ' + (Chars 49))),   # סוגריים: בלעדיהם הפסיק קושר חזק מה-+ והטקסט נחתך   # 8  100 תווים, לא נכנס בשתי שורות
    @(35000, 36000, '   '),                  # 9  ריקה
    @(37000, 39000, '  רווחים   מיותרים  '), # 10
    @(40000, 49000, 'ארוכה בזמן אבל מעט טקסט'),        # 11 9 שניות, קצב נמוך
    @(50000, 59000, ((Chars 160) + ' ' + (Chars 30)))    # 12 9 שניות, הרבה טקסט
)
$d = NewDoc $rows
$before = @(foreach ($c in $d.Cues) { "$($c.Start)|$($c.End)|$($c.Text)" })
$d.Push('תיקון')
$res = $mFix.Invoke($null, (Pack $d))
$cues = @($d.Cues)
function CueWith($needle) { foreach ($c in $d.Cues) { if ($c.Text.Contains($needle)) { return $c } }; return $null }
Check 'אין חפיפות אחרי התיקון' ($d.CountOverlaps() -eq 0) ''
$c2 = CueWith 'קצרה עם מקום'
Check 'קצרה עם מקום: הוארכה לשנייה' ($c2.End - $c2.Start -eq 1000) ("dur=" + ($c2.End - $c2.Start))
$c3 = CueWith 'קצרה בלי מקום'; $c4 = CueWith 'צמודה'
Check 'קצרה בלי מקום: לא יצרה חפיפה חדשה' ($c3.End -le $c4.Start - 80) ("end=" + $c3.End + " next=" + $c4.Start)
$c5 = $null; foreach ($c in $d.Cues) { if ($c.Start -eq 13000) { $c5 = $c } }
Check 'מהירה עם מקום: הוארכה עד שהקצב קריא' ($c5.Cps -le 20.0001 -and $c5.End -le 20000 - 80) ("cps=" + [Math]::Round($c5.Cps, 1))
$c6 = CueWith 'שורה ארוכה'
$l6 = @($c6.Text.Split("`n"))
Check 'שורה ארוכה: סודרה בשתי שורות של עד 42' ($l6.Count -eq 2 -and ($l6 | Where-Object { $_.Length -gt 42 }).Count -eq 0) ($c6.Text -replace "`n", ' / ')
Check 'בלי לאבד מילה בסידור' (($c6.Text -replace '\s+', ' ') -eq 'שורה ארוכה מאוד שהיא יותר מארבעים ושתיים תווים בוודאות גמורה') ''
Check 'דו-שיח לא נשבר מחדש' ((CueWith '- ').Text -eq $dialog) ''
$c8 = $null; foreach ($c in $d.Cues) { if ($c.Start -eq 28000) { $c8 = $c } }
Check '100 תווים: לא נגעו (לא נכנס בשתי שורות)' ($c8.Text -eq ((Chars 50) + ' ' + (Chars 49))) ''
Check 'כתובית ריקה נמחקה' ($d.Cues.Count -eq $rows.Count - 1 -and $res.Removed -eq 1) ("count=" + $d.Cues.Count)
Check 'רווחים מיותרים נוקו' ((CueWith 'רווחים').Text -eq 'רווחים מיותרים') ((CueWith 'רווחים').Text)
$c11 = CueWith 'ארוכה בזמן'
Check 'ארוכה בזמן עם מעט טקסט: קוצרה ל-7 שניות' ($c11.End - $c11.Start -eq 7000) ''
$c12 = $null; foreach ($c in $d.Cues) { if ($c.Start -eq 50000) { $c12 = $c } }
Check 'ארוכה בזמן עם הרבה טקסט: לא קוצרה (היה יוצא מהיר מדי)' ($c12.End - $c12.Start -eq 9000) ''
$leftKinds = @($res.Left | ForEach-Object { [string]$_.Kind })
Check 'מה שלא תוקן מדווח (שורה של 100 תווים)' ($leftKinds -contains 'LongLine') ($leftKinds -join ',')
$sum = [string]$qaT.GetMethod('Summary', $SF).Invoke($null, (Pack $res))
Check 'סיכום בעברית, אומר מה נשאר ואיך מבטלים' ($sum -match '^[^A-Za-z]*[א-ת]' -and $sum.Contains('ביד') -and $sum.Contains('Ctrl+Z')) $sum
$d.Undo()
$after = @(foreach ($c in $d.Cues) { "$($c.Start)|$($c.End)|$($c.Text)" })
Check 'ביטול אחד מחזיר הכול בדיוק' (($after -join "`n") -eq ($before -join "`n")) ("before=" + $before.Count + " after=" + $after.Count)

# ---- מסמכים אקראיים ----
$rnd = New-Object Random 42
$words = @('שלום', 'תורה', 'והיום', 'נלמד', 'את', 'הסוגיה', 'של', 'ברכות', 'רש"י', 'אומר', 'כך', 'ותוספות', 'חולקים', '-', 'כן')
$unstable = 0; $overl = 0; $lost = 0; $shortest = 99999; $examples = @()
for ($n = 0; $n -lt 300; $n++) {
    $rs = @(); $t = 0
    $count = $rnd.Next(2, 25)
    for ($i = 0; $i -lt $count; $i++) {
        $t += $rnd.Next(-1500, 3000); if ($t -lt 0) { $t = 0 }
        $dur = $rnd.Next(50, 9000)
        $wc = $rnd.Next(0, 22)
        $txt = (@(for ($j = 0; $j -lt $wc; $j++) { $words[$rnd.Next($words.Count)] }) -join ' ')
        if ($rnd.Next(5) -eq 0) { $txt = $txt -replace ' ', "`n" }
        $rs += ,@($t, ($t + $dur), $txt, ($rnd.Next(10) -eq 0))
    }
    $dd = NewDoc $rs
    $dd.Sort()
    $durBefore = @{}; foreach ($c in $dd.Cues) { $durBefore[$c] = $c.End - $c.Start }
    $wordsBefore = (@(foreach ($c in $dd.Cues) { ($c.Text -split '\s+' | Where-Object { $_ }) -join ' ' }) | Where-Object { $_ }) -join ' | '
    [void]$mFix.Invoke($null, (Pack $dd))
    if ($dd.CountOverlaps() -ne 0) { $overl++ }
    # התיקון לא מקצר אף כתובית מתחת ל-100ms. כתובית שנוצרה קצרה ואין לה מקום נשארת כמו שהיא.
    foreach ($c in $dd.Cues) { if ($null -eq $durBefore[$c]) { $shortest = -99999; break }; $floor = [Math]::Min(100, $durBefore[$c]); $gap = ($c.End - $c.Start) - $floor; if ($gap -lt $shortest) { $shortest = $gap } }
    $wordsAfter = (@(foreach ($c in $dd.Cues) { ($c.Text -split '\s+' | Where-Object { $_ }) -join ' ' }) | Where-Object { $_ }) -join ' | '
    if ($wordsAfter -ne $wordsBefore) { $lost++; if ($examples.Count -lt 2) { $examples += "`n     before: $wordsBefore`n     after:  $wordsAfter" } }
    $snap = @(foreach ($c in $dd.Cues) { "$($c.Start)|$($c.End)|$($c.Text)" }) -join "`n"
    $r2 = $mFix.Invoke($null, (Pack $dd))
    $snap2 = @(foreach ($c in $dd.Cues) { "$($c.Start)|$($c.End)|$($c.Text)" }) -join "`n"
    if ($snap2 -ne $snap) { $unstable++ }
}
Check '300 מסמכים אקראיים: אף חפיפה אחרי התיקון' ($overl -eq 0) ("עם חפיפה: $overl")
Check 'ואף מילה לא אבדה או זזה בין כתוביות' ($lost -eq 0) ("שונו: $lost" + ($examples -join ''))
Check 'והרצה שנייה לא משנה כלום (התיקון יציב)' ($unstable -eq 0) ("השתנו בהרצה שנייה: $unstable")
Check 'והתיקון לא קיצר אף כתובית מתחת ל-100ms' ($shortest -ge 0) ("חריגה: $shortest")

# ---- מהירות ----
$big = NewDoc (@(for ($i = 0; $i -lt 5000; $i++) { ,@(($i * 2000), ($i * 2000 + 2100), ('משפט מספר ' + $i + ' עם קצת טקסט')) }))
$sw = [Diagnostics.Stopwatch]::StartNew(); $bigIssues = Find $big; $sw.Stop()
Check '5,000 כתוביות נבדקות מהר (הטיימר מריץ את זה פעם בחצי שנייה)' ($sw.ElapsedMilliseconds -lt 150) ("$($sw.ElapsedMilliseconds)ms, בעיות: " + $bigIssues.Count)

# ================= 3. בממשק =================
Write-Host 'בממשק'
function Fld($obj, $name) { return $formT.GetField($name, $IF).GetValue($obj) }
function Call($obj, $name, $argv) { return $formT.GetMethod($name, $IF).Invoke($obj, $argv) }
$f = [Activator]::CreateInstance($formT)
$f.StartPosition = 'Manual'; $f.Location = New-Object Drawing.Point -3000, -3000
$f.ClientSize = New-Object Drawing.Size 1493, 997
$f.Show(); [Windows.Forms.Application]::DoEvents()
$doc = Fld $f '_doc'
foreach ($r in @(@(0, 3000, 'אחת חופפת'), @(2500, 5000, 'שתיים'), @(6000, 7000, (Chars 25)), @(8000, 11000, 'תקינה לגמרי'), @(12000, 13000, (Chars 26)))) {
    $doc.Cues.Add($cueT.GetConstructor([Type[]]@([long], [long], [string])).Invoke(@([long]$r[0], [long]$r[1], [string]$r[2])))
}
[void](Call $f 'SyncAfterDocChange' @())
[void](Call $f 'RefreshQa' (Pack $true)); [Windows.Forms.Application]::DoEvents()
$qa = Fld $f '_qaBtn'
$expected = (Find $doc).Count
Check 'התג מופיע, עם מספר הבעיות' ($qa.Visible -and $qa.Text.Contains([string]$expected)) ("visible=" + $qa.Visible + " text=" + $qa.Text + " expected=$expected")
$card = Fld $f '_listCard'
Check 'התג בכותרת הרשימה, בצד שמאל' ($qa.Parent -eq $card -and $qa.Bottom -le $card.HeaderH -and $qa.Right -lt $card.Width / 2) ("bounds=" + $qa.Bounds)

$items = $formT.GetMethod('SubtitleMenuItems', $IF).Invoke($f, @())
Check '״תיקון תזמונים אוטומטי״ ירד מהתפריט' (-not ($items | Where-Object { $_.Text -like '*תיקון תזמונים*' })) ''
Check 'והחלון הישן נמחק מהקוד' ($null -eq (T 'FixDlg')) ''

# הפריטים בלי לפתוח את התפריט: PopupMenu עושה Activate, וזה היה גונב מיקוד מהמשתמש
$mi = @($formT.GetMethod('QaMenuItems', $IF).Invoke($f, @()))
$texts = @($mi | ForEach-Object { $_.Text })
Check 'התפריט: סוג לכל בעיה שנמצאה, ו״לתקן אוטומטית״' (($texts -join '|') -match 'חופפת' -and ($texts -join '|') -match 'מהיר' -and $texts -contains 'לתקן אוטומטית' -and -not (($texts -join '|') -match 'ריקה')) ($texts -join ' | ')

[void](Call $f 'JumpToIssue' (Pack (K 'TooFast'))); [Windows.Forms.Application]::DoEvents()
$ed = Fld $f '_editing'
Check 'קפיצה: לכתובית המהירה הראשונה' ($ed -ne $null -and $ed.Start -eq 6000) ("start=" + $(if ($ed) { $ed.Start }))
$hint = Fld $f '_hintLbl'
Check 'ושורת המצב מסבירה אותה' ($hint.Text -match '^כתובית' -and $hint.Text.Contains('תווים בשנייה')) $hint.Text
[void](Call $f 'JumpToIssue' (Pack (K 'TooFast')))
Check 'קפיצה שנייה: לבאה' ((Fld $f '_editing').Start -eq 12000) ''
[void](Call $f 'JumpToIssue' (Pack (K 'TooFast')))
Check 'ובסוף חוזרת להתחלה' ((Fld $f '_editing').Start -eq 6000) ''

$hint.Text = 'הודעה אחרת שלא קשורה'
[void](Call $f 'RefreshQa' (Pack $false))
Check 'ההסבר לא דורס הודעה שמישהו אחר כתב' ($hint.Text -eq 'הודעה אחרת שלא קשורה') $hint.Text

$list = Fld $f '_list'
$tipOn = [string]$list.GetType().GetMethod('IssueTip', $IF).Invoke($list, (Pack ([int]0) ([int]($list.Width - 30))))
$tipOff = [string]$list.GetType().GetMethod('IssueTip', $IF).Invoke($list, (Pack ([int]3) ([int]($list.Width - 30))))
Check 'ריחוף על הסימן ברשימה מסביר' ($tipOn.Contains('שתיהן על המסך')) $tipOn
Check 'ובשורה תקינה אין הסבר' ($tipOff -eq '') $tipOff

# בחירת כתובית תקינה מחזירה את הרמז הרגיל
$hint.Text = ''; [void](Call $f 'JumpToIssue' (Pack (K 'TooFast')))
foreach ($c in $doc.Cues) { $c.Selected = ($c.Start -eq 8000) }
[void](Call $f 'LoadEditor' @()); [void](Call $f 'RefreshQa' (Pack $false))
Check 'בחירה בכתובית תקינה: ההסבר יורד' (-not ($hint.Text -match '^כתובית')) $hint.Text

$undoBefore = $doc.CanUndo
[void](Call $f 'FixProblems' @()); [Windows.Forms.Application]::DoEvents()
Check 'לתקן אוטומטית: בלי חפיפות, והתג מתעדכן' ($doc.CountOverlaps() -eq 0 -and ((-not $qa.Visible) -or $qa.Text.Contains([string](Find $doc).Count))) ("tag=" + $qa.Text + " visible=" + $qa.Visible)
Check 'ושורת המצב אומרת מה תוקן' ($hint.Text.Contains('נפתרו') -or $hint.Text.Contains('הוארכו') -or $hint.Text.Contains('נפתרה')) $hint.Text
$doc.Undo(); [void](Call $f 'SyncAfterDocChange' @()); [void](Call $f 'RefreshQa' (Pack $true))
Check 'Ctrl+Z מחזיר את הבעיות' ($doc.CountOverlaps() -eq 1 -and $qa.Visible) ''

$doc.Cues.Clear(); $doc.Cues.Add($cueT.GetConstructor([Type[]]@([long], [long], [string])).Invoke(@([long]0, [long]3000, [string]'תקינה')))
[void](Call $f 'SyncAfterDocChange' @()); $doc.ClearHistory()
[void](Call $f 'RefreshQa' (Pack $true))
Check 'בלי בעיות: התג נעלם' (-not $qa.Visible) ''
[void](Call $f 'FixProblems' @())
Check 'ותיקון שלא שינה כלום לא נכנס לביטול' (-not $doc.CanUndo) ''

# ---- הצ׳אט ----
$doc.Cues.Clear()
foreach ($r in @(@(0, 3000, 'אחת'), @(2000, 5000, 'שתיים'))) { $doc.Cues.Add($cueT.GetConstructor([Type[]]@([long], [long], [string])).Invoke(@([long]$r[0], [long]$r[1], [string]$r[2]))) }
[void](Call $f 'SyncAfterDocChange' @())
$fp = Call $f 'AiFindProblems' @()
Check 'בצ׳אט find_problems: כמה, מאיזה סוג, והסבר' ($fp['total'] -eq 1 -and $fp['by_kind']['Overlap'] -eq 1 -and ([string]$fp['first'][0]['explain']).Length -gt 10) ("total=" + $fp['total'])
$ft = Call $f 'AiFixTimings' @()
Check 'ו-fix_timings משתמש באותו תיקון' ($doc.CountOverlaps() -eq 0 -and ([string]$ft['done']).Contains('נפתרה')) ([string]$ft['done'])

$f.Close(); $f.Dispose(); [Windows.Forms.Application]::DoEvents()
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
