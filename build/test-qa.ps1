# בדיקת שגיאות (Qa): הכללים, התיקון האוטומטי, והממשק שמחליף את ״תיקון תזמונים אוטומטי״.
#
# **מה חשוב כאן:**
# - הגבולות בדיוק: 20 תווים בשנייה לא מסומן ו-20.8 כן, 42 תווים בשורה לא ו-43 כן.
#   הספים חייבים להיות אותם ספים שהרשימה צובעת בהם, אחרת התג והצבע סותרים.
# - התיקון לא יוצר בעיה חדשה בדרך: הארכה רק לתוך המקום הפנוי, וסידור שורות
#   לא נוגע בדו-שיח.
# - **התיקון יציב:** הרצה שנייה לא משנה כלום. נבדק על 300 מסמכים אקראיים.
# - ההסבר בשורת המצב לא דורס הודעה שמישהו אחר כתב.
# צפוי: 93 בדיקות.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\Subtext.exe'
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
# תיקון מהצ׳אט מסיר את הסימון ״לא בטוח״, כמו עריכה ביד (0.8.5)
$doc.Cues[0].Doubt = 'Amonic'
$call = [Activator]::CreateInstance((T 'AiCall')); $call.Name = 'edit_cue'; $call.Args['index'] = 1; $call.Args['text'] = 'אחת מתוקנת'
[void](Call $f 'AiEditCue' (Pack $call))
Check 'בצ׳אט edit_cue: התיקון מסיר את הסימון של מילה לא בטוחה' ($doc.Cues[0].Text -eq 'אחת מתוקנת' -and $doc.Cues[0].Doubt -eq '') ('doubt=' + $doc.Cues[0].Doubt)
# הקלדה בשדה הזמן נכנסת לביטול (0.8.5). עד אז Ctrl+Z דילג עליה וביטל את מה שלפניה
$doc.ClearHistory()
foreach ($c in $doc.Cues) { $c.Selected = ($c.Start -eq 0) }
[void](Call $f 'LoadEditor' @())
(Fld $f '_startF').Text = '00:00.5'
$typed = $doc.Cues[0].Start
$doc.Undo()
Check 'זמן שהוקלד בשדה - Ctrl+Z מחזיר אותו' ($typed -eq 500 -and $doc.Cues[0].Start -eq 0) ("typed=$typed after undo=" + $doc.Cues[0].Start)

$f.Close(); $f.Dispose(); [Windows.Forms.Application]::DoEvents()

# ---- סידור אחרי מכונה (Qa.Tidy, 0.8.2) ----
# המקרים מסרטון אמיתי: תמלול + תרגום + צריבה של 94 שניות. הזמנים כאן הם הזמנים שיצאו שם.
Write-Host 'סידור אחרי תמלול ותרגום'
$mTidy = $qaT.GetMethod('Tidy', $SF, $null, [Type[]]@($docT, [bool]), $null)
$mTidy3 = $qaT.GetMethod('Tidy', $SF, $null, [Type[]]@($docT, [bool], [bool]), $null)
function Tidy($doc, [bool]$fromTranscript) { return $mTidy.Invoke($null, (Pack $doc $fromTranscript)) }
$long = 'Your Grace, House Open AI humbly requests a mere million GPUs and I shall deliver it AGI this year.'
$d = NewDoc @(
    @(30530, 32240, 'He''s not building AGI, he''s stealing my features.'),
    @(35410, 35790, 'Dots is a mere Grokbot clone.'),
    @(36910, 37620, 'Slander!'),
    @(19560, 28360, $long),
    @(83120, 83580, 'So,'),
    @(84460, 85590, 'who''s paying in advance?'))
$tr = Tidy $d $true
$cs = @($d.Cues)
$so = @($cs | Where-Object { $_.Text -like 'So,*' })
Check 'שבר ״So,״ מתאחד עם ההמשך שלו, מתחילת השבר' ($so.Count -eq 1 -and $so[0].Text -eq 'So, who''s paying in advance?' -and $so[0].Start -eq 83120) (($so | ForEach-Object { $_.Text + '@' + $_.Start }) -join ' | ')
$parts = @($cs | Where-Object { $_.Start -ge 19000 -and $_.Start -lt 29000 })
$joined = (($parts | ForEach-Object { $_.Text -replace '\r?\n', ' ' }) -join ' ')
$maxDur = ($parts | ForEach-Object { $_.End - $_.Start } | Measure-Object -Maximum).Maximum
Check 'משפט של 8.8 שניות מתפצל, בלי לאבד מילה, וכל חלק עד 7 שניות' ($parts.Count -eq 2 -and $joined -eq $long -and $maxDur -le 7000) ("parts=" + $parts.Count + " max=" + $maxDur + " | " + (($parts | ForEach-Object { $_.Text -replace '\r?\n', ' ' }) -join ' / '))
Check 'והחיתוך במקום טבעי (לא באמצע ״mere million״)' ($parts.Count -eq 2 -and -not ($parts[0].Text -replace '\r?\n', ' ').EndsWith('mere')) ($parts[0].Text -replace '\r?\n', ' ')
$dots = @($cs | Where-Object { $_.Text -like 'Dots*' })[0]
$prevEnd = @($cs | Where-Object { $_.Text -like 'He*' })[0].End
Check 'כתובית של 0.38 שניות: לפחות 1.4 שניות על המסך' ($dots.End - $dots.Start -ge 1400) ("{0}-{1}" -f $dots.Start, $dots.End)
Check 'בלי לגעת בכתובית הבאה (שני פריימים רווח)' ($dots.End -le 36910 - 80) ($dots.End)
Check 'וההתחלה הוקדמה לכל היותר בחצי שנייה' ($dots.Start -ge 35410 - 500 -and $dots.Start -gt $prevEnd) ("start=" + $dots.Start + " prevEnd=" + $prevEnd)
Check 'אין חפיפות אחרי הסידור' ($d.CountOverlaps() -eq 0) ''
$fast = @(Find $d | Where-Object { [string]$_.Kind -eq 'TooFast' -or [string]$_.Kind -eq 'TooShort' })
Check 'אף כתובית לא מהירה מדי ולא קצרה מדי' ($fast.Count -eq 0) (($fast | ForEach-Object { [string]$_.Kind + '@' + $_.Index }) -join ', ')
# בתרגום: אין השהיה נוספת - היא כבר ניתנה בתמלול. רק זמן קריאה ושורות
$d2 = NewDoc @(@(1000, 4000, 'הייתם קרובים במשך שלושה חורפים.'), @(9000, 11000, 'אז תאר לעצמך כמה קרובים אנחנו עכשיו.'))
[void](Tidy $d2 $false)
Check 'בתרגום: כתובית שנקראת בנוחות לא מוארכת' ($d2.Cues[0].End -eq 4000) ($d2.Cues[0].End)
$d3 = NewDoc (,@(0, 7000, 'הוד מעלתך, בית Open AI מבקש מיליון מעבדי GPU, ואני אספק AGI השנה.'))
[void](Tidy $d3 $false)
$l3 = @($d3.Cues[0].Text -split '\r?\n')
Check 'שורה של 68 תווים נשברת לשתיים מאוזנות' ($l3.Count -eq 2 -and ($l3 | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum -le 42) (($l3) -join ' / ')
$again = Tidy $d $true
# שארית שצמודה מכל צד (״בגדול.״ 0.6 שניות): מתאחדת עם השכנה, באותה שורה
$d4 = NewDoc @(@(69280, 70320, 'שמעתי.'), @(70400, 70650, 'בגדול.'), @(71070, 72530, 'תשכח מהחברות האלה, ג''נסן.'))
[void]$mTidy3.Invoke($null, (Pack $d4 $true $false))
Check 'שארית צמודה מתאחדת עם השכנה, משפט בכל שורה: ״שמעתי.״ מעל ״בגדול.״' ($d4.Cues.Count -eq 2 -and ($d4.Cues[0].Text -replace "`r?`n", '|') -eq 'שמעתי.|בגדול.' -and $d4.Cues[0].End -ge 70650) ((@($d4.Cues) | ForEach-Object { $_.Text + '@' + $_.Start + '-' + $_.End }) -join ' | ')
# מילת פתיחה שנשארה בסוף כתובית (״הקיבולת שלנו מוגבלת, אז,״ בסרטון האמיתי): שייכת לבאה
$d5 = NewDoc @(@(81410, 83540, 'Our capacity is limited. So,'), @(84460, 86210, 'who''s paying in advance?'))
[void]$mTidy3.Invoke($null, (Pack $d5 $true $false))
Check 'מילת פתיחה בסוף כתובית: מתאחדת עם הבאה, משפט בכל שורה' ($d5.Cues.Count -eq 1 -and ($d5.Cues[0].Text -replace "`r?`n", '|') -eq 'Our capacity is limited.|So, who''s paying in advance?') ((@($d5.Cues) | ForEach-Object { $_.Text -replace "`r?`n", '|' }) -join ' / ')
$d6 = NewDoc @(@(1000, 4000, 'This sentence is long enough to fill most of a line. But,'), @(4500, 8000, 'the next one is also long and fills almost the whole line.'))
[void]$mTidy3.Invoke($null, (Pack $d6 $true $false))
Check 'וכשלא נכנס יחד - מילת הפתיחה עוברת לתחילת הבאה' ($d6.Cues.Count -eq 2 -and ($d6.Cues[0].Text -replace "`r?`n", ' ') -eq 'This sentence is long enough to fill most of a line.' -and ($d6.Cues[1].Text -replace "`r?`n", ' ').StartsWith('But, the next one')) ((@($d6.Cues) | ForEach-Object { $_.Text -replace "`r?`n", '|' }) -join ' / ')
$d7 = NewDoc @(@(1000, 3000, 'We spoke with Mr. Smith,'), @(3200, 5000, 'and he agreed.'))
[void]$mTidy3.Invoke($null, (Pack $d7 $true $false))
Check '״Mr.״ הוא לא סוף משפט - שום דבר לא זז' ($d7.Cues.Count -eq 2 -and $d7.Cues[0].Text -eq 'We spoke with Mr. Smith,') ((@($d7.Cues) | ForEach-Object { $_.Text }) -join ' / ')
# שארית קצרה בלי סוף משפט מתאחדת עם הבאה, לא עם הקודמת
$mLeft = $qaT.GetMethod('MergeLeftovers', [Reflection.BindingFlags]'NonPublic,Static')
$d8 = NewDoc @(@(81400, 82970, 'Our capacity is limited.'), @(83170, 83530, 'So,'), @(83600, 86000, 'who''s paying in advance?'))
[void]$mLeft.Invoke($null, (Pack $d8.Cues))
Check 'שבר קצר (״So,״) מתאחד עם מה שאחריו' ($d8.Cues.Count -eq 2 -and $d8.Cues[1].Text -eq 'So, who''s paying in advance?' -and $d8.Cues[1].Start -eq 83170) ((@($d8.Cues) | ForEach-Object { $_.Text + '@' + $_.Start }) -join ' / ')
# פיצול לפי זמן הדיבור: הפסקה דרמטית לפני ״million״ לא מקדימה את ״and I shall״ (נאמר ב-26.0)
$gapT = $asm.GetType('SubtitleStudio.AutoTime+Gap'); $gapListT = [Collections.Generic.List``1].MakeGenericType($gapT)
$funcT = [Func``3].MakeGenericType([long], [long], $gapListT)
function GapsFn($pairs) {
    $l = [Activator]::CreateInstance($gapListT)
    foreach ($p in $pairs) { $g = [Activator]::CreateInstance($gapT); $g.Start = [long]$p[0]; $g.End = [long]$p[1]; [void]$gapListT.GetMethod('Add').Invoke($l, (Pack $g)) }
    $sb = { param([long]$a, [long]$b) return ,$l }.GetNewClosure()
    return ($sb -as $funcT)
}
$mSplit = $qaT.GetMethod('SplitLong', [Reflection.BindingFlags]'NonPublic,Static', $null, [Type[]]@($d8.Cues.GetType(), $funcT), $null)
$long = 'Your grace, House Open AI humbly requests a mere million GPUs, and I shall deliver it AGI this year.'
$d9 = NewDoc @(,@(20440, 28320, $long))
[void]$mSplit.Invoke($null, (Pack $d9.Cues (GapsFn @(,@(24000, 24800)))))
Check 'פיצול לפי זמן הדיבור: ״and I shall״ מתחיל קרוב ל-26.0, לא בהפסקה של 24.0' ($d9.Cues.Count -eq 2 -and $d9.Cues[1].Text.StartsWith('and I shall') -and [Math]::Abs($d9.Cues[1].Start - 25729) -le 60) ((@($d9.Cues) | ForEach-Object { $_.Text.Substring(0, 12) + '@' + $_.Start + '-' + $_.End }) -join ' / ')
$d10 = NewDoc @(,@(0, 7500, 'Aaaa aaaa aaaa aaaa aaaa aaaa aaaa aaaa aaaa aa. Bbbb bbbb bbbb bbbb bbbb bbbb bbbb bbbb bbbb bb.'))
[void]$mSplit.Invoke($null, (Pack $d10.Cues (GapsFn @(,@(3500, 4000)))))
Check 'והפסקה ממש במקום הצפוי - הפיצול נופל בתוכה' ($d10.Cues.Count -eq 2 -and $d10.Cues[0].End -eq 3680 -and $d10.Cues[1].Start -eq 3880) ((@($d10.Cues) | ForEach-Object { '' + $_.Start + '-' + $_.End }) -join ' / ')
# הסרטון האמיתי: ההפסקה היחידה שנמצאה היא 25.70-26.24, וההערכה 25.19 - בפיצול בפסיק היא נלקחת
$dr = NewDoc @(,@(20400, 28500, $long))
[void]$mSplit.Invoke($null, (Pack $dr.Cues (GapsFn @(,@(25700, 26240)))))
Check 'פיצול בפסיק: ההפסקה הקרובה נלקחת גם כשההערכה רחוקה ממנה בחצי שנייה' ($dr.Cues.Count -eq 2 -and $dr.Cues[0].End -eq 25880 -and $dr.Cues[1].Start -eq 26120) ((@($dr.Cues) | ForEach-Object { '' + $_.Start + '-' + $_.End }) -join ' / ')
# ״Our capacity is limited, so,״ - מילת פתיחה אחרי פסיק, לא אחרי נקודה
$dc = NewDoc @(@(81410, 84200, 'Our capacity is limited, so,'), @(84400, 86600, 'who''s paying in advance?'))
[void]$mTidy3.Invoke($null, (Pack $dc $true $false))
Check 'מילת פתיחה אחרי פסיק (״limited, so,״) - גם עוברת' ($dc.Cues.Count -eq 1 -and ($dc.Cues[0].Text -replace "`r?`n", '|') -eq 'Our capacity is limited,|so, who''s paying in advance?') ((@($dc.Cues) | ForEach-Object { $_.Text -replace "`r?`n", '|' }) -join ' / ')
# שני דוברים (0.8.4): לא מתאחדים כמשפט אחד; כתובית קצרה של דובר אחד ליד של אחר - דו-שיח עם מקפים
$dsp = NewDoc @(@(64400, 65000, 'אדוני הנשיא.'), @(65100, 68500, 'אויבינו מעבר לים מתקדמים.'))
$dsp.Cues[0].Actor = '1'; $dsp.Cues[1].Actor = '2'
[void]$mLeft.Invoke($null, (Pack $dsp.Cues))
Check 'שני דוברים בכתובית אחת: שורה לכל אחד, עם מקף' ($dsp.Cues.Count -eq 1 -and ($dsp.Cues[0].Text -replace "`r?`n", '|') -eq '- אדוני הנשיא.|- אויבינו מעבר לים מתקדמים.') ((@($dsp.Cues) | ForEach-Object { $_.Text -replace "`r?`n", '|' }) -join ' / ')
$dsp2 = NewDoc @(@(81410, 83540, 'Our capacity is limited. So,'), @(84460, 86210, 'who''s paying in advance?'))
$dsp2.Cues[0].Actor = '1'; $dsp2.Cues[1].Actor = '2'
[void]$mTidy3.Invoke($null, (Pack $dsp2 $true $false))
Check 'מילת פתיחה לא עוברת לדובר אחר' ($dsp2.Cues.Count -eq 2 -and $dsp2.Cues[0].Text -eq 'Our capacity is limited. So,') ((@($dsp2.Cues) | ForEach-Object { $_.Text -replace "`r?`n", '|' }) -join ' / ')
$dsp3 = NewDoc @(@(1000, 1400, 'So,'), @(1600, 3000, 'who is paying?'))
$dsp3.Cues[0].Actor = '1'; $dsp3.Cues[1].Actor = '2'
$mFrag = $qaT.GetMethod('MergeFragments', [Reflection.BindingFlags]'NonPublic,Static')
[void]$mFrag.Invoke($null, (Pack $dsp3.Cues))
Check 'שבר של דובר אחד לא מתאחד עם משפט של אחר' ($dsp3.Cues.Count -eq 2) ((@($dsp3.Cues) | ForEach-Object { $_.Text }) -join ' / ')
# פנייה (״Mr. President,״) שהמודל הפריד - לא מתאחדת עם המשפט שאחריה (0.8.4); ״So,״ כן
$dv = NewDoc @(@(64100, 65400, 'Mr. President,'), @(65500, 68800, 'our enemies across the water are making progress.'))
[void]$mFrag.Invoke($null, (Pack $dv.Cues))
Check 'פנייה שהמודל הפריד נשארת לבד' ($dv.Cues.Count -eq 2) ((@($dv.Cues) | ForEach-Object { $_.Text }) -join ' / ')
$dv2 = NewDoc @(@(1000, 1400, 'And so,'), @(1600, 3000, 'we continue.'))
[void]$mFrag.Invoke($null, (Pack $dv2.Cues))
Check 'שבר שמתחיל במילת פתיחה (״And so,״) - כן מתאחד' ($dv2.Cues.Count -eq 1 -and $dv2.Cues[0].Text -eq 'And so, we continue.') ((@($dv2.Cues) | ForEach-Object { $_.Text }) -join ' / ')
# מילה שהמודל לא היה בטוח בה (0.8.3)
$du = NewDoc @(@(1000, 3000, 'Only house Anthropic!'), @(4000, 6000, 'שורה רגילה.'))
$du.Cues[0].Doubt = 'Anthropic'
Check 'מילה לא בטוחה מופיעה כבעיה, עם המילה' ((Has $du 0 'Unsure') -and -not (Has $du 1 'Unsure')) ((Kinds $du 0) -join ',')
$ux = @(Find $du | Where-Object { [string]$_.Kind -eq 'Unsure' })[0]
Check 'וההסבר נוקב במילה' ([string]$qaT.GetMethod('Explain', [Reflection.BindingFlags]'Public,Static').Invoke($null, (Pack $ux)) -match 'Anthropic') ''
$dm = NewDoc @(@(68900, 69480, 'I have heard.'), @(69720, 70480, 'Bigly.'))
$dm.Cues[1].Doubt = 'Bigly'
[void]$mLeft.Invoke($null, (Pack $dm.Cues))
Check 'שתי כתוביות שמתאחדות - הסימון נשמר' ($dm.Cues.Count -eq 1 -and $dm.Cues[0].Doubt -eq 'Bigly') ($dm.Cues[0].Doubt)
$ds = NewDoc @(,@(20440, 28320, $long))
$ds.Cues[0].Doubt = 'deliver'
[void]$mSplit.Invoke($null, (Pack $ds.Cues $null))
Check 'כתובית שמתפצלת - הסימון עובר לחלק שבו המילה' ($ds.Cues.Count -eq 2 -and $ds.Cues[0].Doubt -eq '' -and $ds.Cues[1].Doubt -eq 'deliver') ($ds.Cues[0].Doubt + ' / ' + $ds.Cues[1].Doubt)
# איחוד ביד, מהתפריט או מהעוזר (0.8.5): עד אז הסימון של הכתוביות שאוחדו אבד, ושני דוברים נדבקו לשורה אחת
$mInto = $qaT.GetMethod('MergeInto', [Reflection.BindingFlags]'NonPublic,Static')
$dj = NewDoc @(@(1000, 2000, 'Only house'), @(2100, 3500, 'Anthropic!'))
$dj.Cues[1].Doubt = 'Anthropic'
[void]$mInto.Invoke($null, (Pack $dj.Cues))
Check 'איחוד ביד: כל הטקסט, עד הסוף של האחרונה, והסימון שלה' ($dj.Cues[0].Text -eq 'Only house Anthropic!' -and $dj.Cues[0].End -eq 3500 -and $dj.Cues[0].Doubt -eq 'Anthropic') ($dj.Cues[0].Text + ' | ' + $dj.Cues[0].Doubt)
$dk = NewDoc @(@(1000, 2000, 'אדוני הנשיא,'), @(2100, 3500, 'האויבים שלנו מתקדמים.'))
$dk.Cues[0].Actor = '1'; $dk.Cues[1].Actor = '2'
[void]$mInto.Invoke($null, (Pack $dk.Cues))
Check 'איחוד ביד של שני דוברים: שורה לכל אחד, עם מקף' (($dk.Cues[0].Text -replace "`r?`n", '|') -eq '- אדוני הנשיא,|- האויבים שלנו מתקדמים.' -and $dk.Cues[0].Actor -eq '1|2') (($dk.Cues[0].Text -replace "`r?`n", '|') + ' actor=' + $dk.Cues[0].Actor)
Check 'סידור שני לא משנה כלום (יציב)' ($again.Merged -eq 0 -and $again.Split -eq 0 -and $again.Rewrapped -eq 0) ("merged=" + $again.Merged + " split=" + $again.Split)
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
