# תקציר העדכון: מה רואה מי שקופץ מכל גרסה היסטורית.
$ErrorActionPreference = 'Continue'
$env:SUBSTUDIO_TEST = '1'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes('D:\Claude\subtitle-studio\dist\SubtitleStudio.exe'))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
$cl = $asm.GetType('SubtitleStudio.Changelog')
$build = $cl.GetMethod('Build', $ST)
$json = [IO.File]::ReadAllText('D:\Claude\subtitle-studio\changelog.json', [Text.Encoding]::UTF8)

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }
function F($o,$n){ $o.GetType().GetField($n,$IN).GetValue($o) }

$all = @('0.1.0','0.1.1','0.2.0','0.2.1','0.3.0','0.3.1','0.4.0','0.5.0','0.5.1','0.5.2','0.6.0','0.6.1','0.6.2','0.6.3')
$to  = '0.6.3'

Write-Host 'תקציר לכל קפיצה'
foreach ($from in $all) {
    $s = $build.Invoke($null, @([string]$json, [string]$from, [string]$to))
    if ($from -eq $to) {
        Check "מ-$from (הגרסה העדכנית)" ((F $s 'Jump') -eq 0) 'אין מה להציע'
        continue
    }
    $jump = F $s 'Jump'
    $groups = F $s 'Groups'
    $lines = 0
    foreach ($g in $groups) { $lines += (F $g 'Items').Count }
    $head = F $s 'Headline'
    Check "מ-$from" (($jump -gt 0) -and ($groups.Count -gt 0) -and $head) ("קפיצה=$jump קבוצות=" + $groups.Count + " שורות=$lines")
    Write-Host ("        " + $head) -ForegroundColor DarkGray
    foreach ($g in $groups) {
        Write-Host ("          [" + (F $g 'Title') + "]") -ForegroundColor DarkCyan
        foreach ($i in (F $g 'Items')) { Write-Host ("            - " + $i) -ForegroundColor DarkGray }
    }
}

Write-Host ''
Write-Host 'הכללים עצמם'
$s1 = $build.Invoke($null, @([string]$json, '0.6.2', '0.6.3'))
Check 'קפיצה של אחת = הכול' ((F $s1 'Jump') -eq 1) (F $s1 'Headline')
$shown1 = 0; foreach ($g in (F $s1 'Groups')) { $shown1 += (F $g 'Items').Count }
Check 'ואף פריט לא הושמט' ($shown1 -eq ((F $s1 'Majors') + (F $s1 'Features') + (F $s1 'Fixes'))) "מוצג=$shown1"

$s9 = $build.Invoke($null, @([string]$json, '0.1.0', '0.6.3'))
$shown9 = 0; foreach ($g in (F $s9 'Groups')) { if ((F $g 'Title') -ne 'ובנוסף') { $shown9 += (F $g 'Items').Count } }
Check 'קפיצה גדולה מקוצרת' ($shown9 -lt ((F $s9 'Majors') + (F $s9 'Features') + (F $s9 'Fixes'))) ("מוצג=$shown9 מתוך " + ((F $s9 'Majors') + (F $s9 'Features') + (F $s9 'Fixes')))
Check 'ויש שורת סיכום' (((F $s9 'Groups') | Where-Object { (F $_ 'Title') -eq 'ובנוסף' }).Count -eq 1) ''

# **התקרה** יורדת ככל שהקפיצה גדלה. לא מספר השורות בפועל: מהדורה
# קטנה תיתן פחות שורות מקפיצה גדולה גם בלי שום קיצור, וזה תקין.
# מה שחייב להתקיים הוא שאף קפיצה לא חורגת מהתקרה שלה, ושהתקרות
# עצמן לא עולות. הבדיקה הקודמת כאן השוותה שורות בפועל, נכשלה
# בצדק - וחשפה שקפיצה של 2-3 לא הוגבלה כלל.
function Cap($jump) {
    if ($jump -le 1) { return 999 }
    if ($jump -le 3) { return 999 }   # ״חשוב לדעת״ במלואו, תוספות עד 6
    if ($jump -le 6) { return 7 }
    return 5
}
$within = $true
foreach ($from in $all) {
    if ($from -eq $to) { continue }
    $s = $build.Invoke($null, @([string]$json, [string]$from, [string]$to))
    $n = 0
    foreach ($g in (F $s "Groups")) { if ((F $g "Title") -ne "ובנוסף") { $n += (F $g "Items").Count } }
    if ($n -gt (Cap (F $s "Jump"))) { $within = $false; Write-Host ("        חריגה מ-$from : $n") -ForegroundColor Red }
}
Check 'אף קפיצה לא חורגת מהתקרה שלה' $within ''

# ותקרת התוספות בקפיצה קטנה קיימת בכלל
$s23 = $build.Invoke($null, @([string]$json, "0.6.0", [string]$to))
$feat = 0
foreach ($g in (F $s23 "Groups")) { if ((F $g "Title") -eq "מה חדש") { $feat = (F $g "Items").Count } }
Check 'תוספות בקפיצה של 2-3 מוגבלות ל-6' ($feat -le 6) ("בפועל=$feat")

Write-Host ''
Write-Host 'מקרי גבול'
$sn = $build.Invoke($null, @([string]$json, '9.9.9', '9.9.9'))
Check 'גרסה עתידית: אין פריטים' ((F $sn 'Jump') -eq 0) ''
$sb = $build.Invoke($null, @([string]'not json at all', '0.1.0', '0.6.3'))
Check 'JSON שבור מחזיר null' ($sb -eq $null) ''
$se = $build.Invoke($null, @([string]'{}', '0.1.0', '0.6.3'))
Check 'JSON בלי versions מחזיר null' ($se -eq $null) ''
$sc = $build.Invoke($null, @([string]$json, '0.5.0', '0.5.2'))
Check 'חסם עליון נשמר' (((F $sc 'Versions') -notcontains '0.6.0') -and ((F $sc 'Jump') -eq 2)) ("גרסאות=" + ((F $sc 'Versions') -join ','))
$sv = $build.Invoke($null, @([string]$json, 'v0.6.2', 'v0.6.3'))
Check 'תגית עם v מובילה' ((F $sv 'Jump') -eq 1) ''

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
