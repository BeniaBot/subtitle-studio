# קובץ הפרויקט (.subtext), השמירה האוטומטית והשחזור.
#
# **למה כל כך הרבה:** זה הקוד שמחזיק את העבודה של המשתמש. באג כאן לא
# מציג משהו עקום - הוא מאבד שעה של תזמון. לכן:
# 1. **הלוך-חזור** של כל שדה, עם הטקסטים הכי מעצבנים (מרכאות, לוכסן,
#    שורה חדשה, מפרידי שורה של יוניקוד, אימוג'י, עברית).
# 2. **קבצים שבורים**: 300 שיבושים אקראיים של פרויקט תקין - אסור שאחד
#    מהם יפיל את התוכנה. מקבלים פרויקט או הודעת שגיאה, וזהו.
# 3. **נתיבים**: פרויקט שעבר תיקייה, סרט שנמחק, שמות עם # ו-% (‏Uri
#    שובר אותם בשקט, ולכן החישוב ידני).
# 4. **בתוך החלון**: פתיחה אמיתית, בלי טעינת SRT שיושב ליד הסרט, סרט
#    חסר, מה שואלים ביציאה, גיבוי ושחזור.
#
# צפוי: 87 בדיקות. **פחות מזה = משהו דולג.**
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\SubtitleStudio.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$NP = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$prj = T 'Project'; $pdT = T 'ProjectData'; $cueT = T 'Cue'; $styleT = T 'SubStyle'
$ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
$toJson = $prj.GetMethod('ToJson', $ST)
$parseM = $prj.GetMethod('Parse', $ST)
function ParseJ($json) {
    $a = New-Object object[] 2; $a[0] = [string]$json; $a[1] = $null
    $d = $parseM.Invoke($null, $a)
    return @{ Data = $d; Error = $a[1] }
}

$work = [IO.Path]::GetFullPath([string](Join-Path $env:TEMP ('ss-project-' + [guid]::NewGuid().ToString('N').Substring(0, 8))))
New-Item -ItemType Directory $work | Out-Null

# ================= 1. הלוך-חזור =================
Write-Host 'הלוך-חזור של כל שדה'
$nasty = @(
    'שלום עולם',
    "שתי`nשורות",
    'מרכאות "כפולות" ולוכסן \ הפוך',
    ("טאב`tבאמצע"),
    ('מפריד' + [char]0x2028 + 'שורה' + [char]0x2029 + 'ופסקה'),
    ('אימוג׳י ' + [char]::ConvertFromUtf32(0x1F600) + ' וסוף'),
    ''
)
$d = [Activator]::CreateInstance($pdT)
$t = 1000
foreach ($x in $nasty) {
    $c = $ctor.Invoke(@([long]$t, [long]($t + 1500), [string]$x))
    $d.Cues.Add($c); $t += 2000
}
$d.Cues[1].Untimed = $true
$d.Cues[2].Style = 'Main'
$d.Cues[3].Actor = 'רב #1 ו-50%'
$d.MediaPath = 'D:\שיעורים\שיעור #3 (100%)\סרט.mp4'
$d.MediaRelative = '..\שיעור #3 (100%)\סרט.mp4'
$d.MediaSize = 123456789012
$d.MediaModified = 638617000000000000
$d.SubtitlesPath = 'D:\שיעורים\כתוביות.srt'
$d.SubtitlesRelative = 'כתוביות.srt'
$d.Position = 754321; $d.InPoint = 1000; $d.OutPoint = 99000
$d.Zoomed = $true; $d.PxPerSec = 123.456; $d.ViewStart = 5000; $d.Fps = 29.97
$d.Origin = 'C:\x\y.subtext'
$sty = [Activator]::CreateInstance($styleT)
$sty.FontName = 'Frank Ruhl Libre'; $sty.FontPct = 7.25; $sty.Bold = $false; $sty.Italic = $true
$sty.Primary = [Drawing.Color]::FromArgb(200, 255, 220, 10); $sty.Outline = [Drawing.Color]::FromArgb(255, 1, 2, 3)
$sty.Shadow = [Drawing.Color]::FromArgb(10, 20, 30, 40); $sty.BoxColor = [Drawing.Color]::FromArgb(128, 0, 0, 255)
$sty.OutlineWidth = 3.5; $sty.ShadowDepth = 1.25; $sty.OpaqueBox = $true; $sty.Alignment = 8
$sty.MarginVPct = 9; $sty.MarginHPct = 2.5; $sty.LineSpacing = 1.3
$d.Style = $sty
$cutsL = [Activator]::CreateInstance([Collections.Generic.List[long]])
foreach ($x in @(5333, 9733, 16700)) { $cutsL.Add($x) }
$d.SceneCuts = $cutsL

$json = $toJson.Invoke($null, (Pack $d))
$r = ParseJ $json
$e = $r.Data
Check 'נקרא בחזרה בלי שגיאה' ($e -ne $null -and $r.Error -eq $null) $r.Error
$sameText = $true
for ($i = 0; $i -lt $nasty.Count; $i++) { if ($e.Cues[$i].Text -ne $nasty[$i]) { $sameText = $false; Write-Host ("        שורה $i השתנתה: [" + $e.Cues[$i].Text + "]") } }
Check 'כל הטקסטים זהים, כולל התווים הקשים' $sameText ''
Check 'זמנים' ($e.Cues[3].Start -eq 7000 -and $e.Cues[3].End -eq 8500) ''
Check 'שורה בלי תזמון נשארת בלי תזמון' ($e.Cues[1].Untimed -and -not $e.Cues[0].Untimed) ''
Check 'סגנון ודובר של כתובית' ($e.Cues[2].Style -eq 'Main' -and $e.Cues[3].Actor -eq 'רב #1 ו-50%') ''
Check 'הסרט: נתיב, יחסי, גודל, תאריך' ($e.MediaPath -eq $d.MediaPath -and $e.MediaRelative -eq $d.MediaRelative -and $e.MediaSize -eq 123456789012 -and $e.MediaModified -eq 638617000000000000) ''
Check 'קובץ הכתוביות' ($e.SubtitlesPath -eq $d.SubtitlesPath -and $e.SubtitlesRelative -eq 'כתוביות.srt') ''
Check 'מיקום, טווח, זום, קצב' ($e.Position -eq 754321 -and $e.InPoint -eq 1000 -and $e.OutPoint -eq 99000 -and $e.Zoomed -and [Math]::Abs($e.PxPerSec - 123.456) -lt 1e-9 -and $e.ViewStart -eq 5000 -and [Math]::Abs($e.Fps - 29.97) -lt 1e-9) ''
Check 'origin (לשחזור)' ($e.Origin -eq 'C:\x\y.subtext') ''
$es = $e.Style
Check 'עיצוב: גופן, גודל, מודגש, נטוי' ($es.FontName -eq 'Frank Ruhl Libre' -and $es.FontPct -eq 7.25 -and -not $es.Bold -and $es.Italic) ''
Check 'עיצוב: ארבעה צבעים כולל שקיפות' ($es.Primary.ToArgb() -eq $sty.Primary.ToArgb() -and $es.Outline.ToArgb() -eq $sty.Outline.ToArgb() -and $es.Shadow.ToArgb() -eq $sty.Shadow.ToArgb() -and $es.BoxColor.ToArgb() -eq $sty.BoxColor.ToArgb()) ''
Check 'עיצוב: קו, צל, רקע, יישור, שוליים, ריווח' ($es.OutlineWidth -eq 3.5 -and $es.ShadowDepth -eq 1.25 -and $es.OpaqueBox -and $es.Alignment -eq 8 -and $es.MarginVPct -eq 9 -and $es.MarginHPct -eq 2.5 -and $es.LineSpacing -eq 1.3) ''
Check 'חיתוכי סצנה' ((@($e.SceneCuts) -join ',') -eq '5333,9733,16700') (@($e.SceneCuts) -join ',')
Check 'עברית נכתבת כעברית, לא כקודים' ($json.Contains('שיעור #3') -and -not $json.Contains('\u05')) ''
Check 'כתובית בשורה' (([regex]::Matches($json, '{ "start"')).Count -eq $nasty.Count) ''
$again = $toJson.Invoke($null, (Pack $e))
Check 'כתיבה שנייה זהה לראשונה (חוץ מזמן השמירה)' (($again -replace '"saved": "[^"]*"', '') -eq ($json -replace '"saved": "[^"]*"', '')) ''

$empty = [Activator]::CreateInstance($pdT)
$re = ParseJ ($toJson.Invoke($null, (Pack $empty)))
Check 'פרויקט ריק: בלי סרט, בלי כתוביות, בלי עיצוב' ($re.Data -ne $null -and $re.Data.Cues.Count -eq 0 -and $re.Data.MediaPath -eq $null -and $re.Data.Style -eq $null) $re.Error

# ================= 2. קבצים שבורים =================
Write-Host 'קבצים שבורים'
$r = ParseJ 'זה בכלל לא JSON'
Check 'לא JSON - שגיאה מובנת' ($r.Data -eq $null -and $r.Error -match 'פגום') $r.Error
$r = ParseJ '{"type":"something-else","cues":[]}'
Check 'JSON של משהו אחר' ($r.Data -eq $null -and $r.Error -match 'לא קובץ פרויקט') $r.Error
$r = ParseJ '{"type":"subtext-project","version":2,"cues":[]}'
Check 'גרסת פורמט עתידית - מבקש לעדכן' ($r.Data -eq $null -and $r.Error -match 'חדשה') $r.Error
$r = ParseJ '[1,2,3]'
Check 'מערך במקום אובייקט' ($r.Data -eq $null) $r.Error
$r = ParseJ '{"type":"subtext-project","cues":[{"start":5000,"end":1000,"text":"הפוך"},{"start":-50,"text":"שלילי"},{"text":7},"לא אובייקט"],"style":{"align":42,"color":"ירוק","size":"גדול"},"position":"x","range":[1]}'
$dd = $r.Data
Check 'שדות משובשים לא מפילים' ($dd -ne $null) $r.Error
if ($dd) {
    Check 'סוף לפני התחלה - מתוקן' ($dd.Cues[0].End -eq 5000) ''
    Check 'התחלה שלילית - אפס' ($dd.Cues[1].Start -eq 0) ''
    Check 'טקסט שאינו מחרוזת - ריק, והאובייקט הזר מדולג' ($dd.Cues.Count -eq 3 -and $dd.Cues[2].Text -eq '') ("count=" + $dd.Cues.Count)
    Check 'עיצוב משובש - ברירות מחדל' ($dd.Style.Alignment -eq 2 -and $dd.Style.Primary.ToArgb() -eq [Drawing.Color]::White.ToArgb() -and $dd.Style.FontPct -eq 5) ''
    Check 'מיקום וטווח משובשים - ברירות מחדל' ($dd.Position -eq 0 -and $dd.InPoint -eq -1) ''
}
$rnd = New-Object Random 20260914
$crashes = 0; $loaded = 0; $refused = 0
for ($k = 0; $k -lt 300; $k++) {
    $mode = $rnd.Next(3)
    $pos = $rnd.Next($json.Length)
    if ($mode -eq 0) { $bad = $json.Substring(0, $pos) }
    elseif ($mode -eq 1) { $bad = $json.Remove($pos, 1) }
    else { $chars = '{}[]",:0-tfn\ ' ; $bad = $json.Remove($pos, 1).Insert($pos, [string]$chars[$rnd.Next($chars.Length)]) }
    try { $rr = ParseJ $bad; if ($rr.Data) { $loaded++ } else { $refused++ } }
    catch { $crashes++ }
}
Check '300 שיבושים אקראיים - אף אחד לא מפיל' ($crashes -eq 0) "נטענו=$loaded סורבו=$refused קריסות=$crashes"

# ================= 3. נתיבים =================
Write-Host 'נתיבים'
$rel = $prj.GetMethod('Relative', $ST)
Check 'אותה תיקייה' ($rel.Invoke($null, @('D:\a\b', 'D:\a\b\x.mp4')) -eq 'x.mp4') ''
Check 'תת-תיקייה' ($rel.Invoke($null, @('D:\a', 'D:\a\b\x.mp4')) -eq 'b\x.mp4') ''
Check 'תיקייה אחות' ($rel.Invoke($null, @('D:\a\b', 'D:\a\c\x.mp4')) -eq '..\c\x.mp4') ''
Check 'כונן אחר - אין יחסי' ($rel.Invoke($null, @('D:\a', 'E:\a\x.mp4')) -eq $null) ''
Check '# ו-% ועברית ורווחים' ($rel.Invoke($null, @('D:\שיעור #3', 'D:\שיעור #3\סרט 100%.mp4')) -eq 'סרט 100%.mp4') ''
Check 'אותיות גדולות וקטנות' ($rel.Invoke($null, @('D:\Videos', 'd:\videos\x.mp4')) -eq 'x.mp4') ''

$locate = $prj.GetMethod('Locate', $ST)
$A = [string](Join-Path $work 'A #1'); $B = [string](Join-Path $work 'B 100%')
New-Item -ItemType Directory (Join-Path $A 'media') -Force | Out-Null
$movie = [string](Join-Path $A 'media\סרט.mp4'); [IO.File]::WriteAllText($movie, 'x')
$prjPath = [string](Join-Path $A 'פרויקט.subtext')
$relMovie = $rel.Invoke($null, @($A, $movie))
Check 'יחסי מחושב' ($relMovie -eq 'media\סרט.mp4') $relMovie
Copy-Item $A $B -Recurse
Remove-Item $A -Recurse -Force
$found = $locate.Invoke($null, @([string](Join-Path $B 'פרויקט.subtext'), $movie, $relMovie))
Check 'תיקייה שהועברה כולה - נמצא דרך היחסי' ($found -eq (Join-Path $B 'media\סרט.mp4')) $found
$flat = [string](Join-Path $work 'flat'); New-Item -ItemType Directory $flat | Out-Null
Copy-Item (Join-Path $B 'media\סרט.mp4') $flat
$found = $locate.Invoke($null, @([string](Join-Path $flat 'p.subtext'), $movie, 'nothing\here.mp4'))
Check 'הועברו לתיקייה אחת - נמצא לפי השם' ($found -eq (Join-Path $flat 'סרט.mp4')) $found
$found = $locate.Invoke($null, @([string](Join-Path $work 'nowhere\p.subtext'), [string](Join-Path $B 'media\סרט.mp4'), 'x\y.mp4'))
Check 'יחסי שבור - נמצא דרך המוחלט' ($found -eq (Join-Path $B 'media\סרט.mp4')) $found
$found = $locate.Invoke($null, @([string](Join-Path $work 'p.subtext'), 'Q:\לא\קיים.mp4', 'לא\קיים.mp4'))
Check 'לא קיים בשום מקום - null' ($found -eq $null) ''

$write = $prj.GetMethod('Write', $ST)
$wf = [string](Join-Path $work 'כתיבה #1.subtext')
$write.Invoke($null, @($wf, 'ראשון')) | Out-Null
Check 'כתיבה לקובץ חדש' ([IO.File]::ReadAllText($wf) -eq 'ראשון') ''
$write.Invoke($null, @($wf, 'שני')) | Out-Null
Check 'החלפת קובץ קיים' ([IO.File]::ReadAllText($wf) -eq 'שני') ''
Check 'לא נשאר קובץ זמני' (-not (Test-Path ($wf + '.tmp'))) ''
Check 'בלי BOM (קריא לכל עורך)' ([IO.File]::ReadAllBytes($wf)[0] -ne 0xEF) ''

$same = $prj.GetMethod('SameMedia', $ST)
$fi = New-Object IO.FileInfo (Join-Path $flat 'סרט.mp4')
$pd = [Activator]::CreateInstance($pdT); $pd.MediaSize = $fi.Length; $pd.MediaModified = $fi.LastWriteTimeUtc.Ticks
Check 'אותו סרט - חיתוכים שמורים תקפים' ($same.Invoke($null, @($fi.FullName, $pd))) ''
$fi.LastWriteTimeUtc = $fi.LastWriteTimeUtc.AddMinutes(5)
Check 'הסרט נערך - החיתוכים לא נטענים' (-not $same.Invoke($null, @($fi.FullName, $pd))) ''

# ================= 4. בתוך החלון =================
Write-Host 'בתוך החלון'
$gallery = [string](Join-Path $env:TEMP 'ss-gallery\test.mp4')
if (-not (Test-Path $gallery)) {
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null
}
Check 'סרט הבדיקה קיים' (Test-Path $gallery) $gallery
$media = [string](Join-Path $work 'שיעור #7.mp4')
Copy-Item $gallery $media
(T 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())

$formT = T 'MainForm'
function Fld($o, $n) { return $formT.GetField($n, $NP).GetValue($o) }
function Call($o, $n, $argv) { return $formT.GetMethod($n, $NP).Invoke($o, $argv) }
function NewForm {
    $f = [Activator]::CreateInstance($formT)
    $f.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $f.Location = New-Object Drawing.Point -3000, -3000
    $f.ClientSize = New-Object Drawing.Size 1400, 900
    $f.Show()
    [Windows.Forms.Application]::DoEvents()
    return $f
}
function Pump($ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 } }

$testAuto = [string](Join-Path $env:TEMP 'ss-test-autosave\session.subtext')
if (Test-Path $testAuto) { Remove-Item $testAuto -Force }
$realAuto = [string](Join-Path $env:LOCALAPPDATA 'SubtitleStudio\autosave\session.subtext')
$realBefore = if (Test-Path $realAuto) { (Get-Item $realAuto).LastWriteTimeUtc.Ticks } else { 0 }
$f = NewForm
$formT.GetMethod('OpenMedia', $NP, $null, [Type[]]@([string]), $null).Invoke($f, (Pack ([string]$media))) | Out-Null
Pump 400
$doc = Fld $f '_doc'
foreach ($p in @(@(500, 1500, 'אחת', $false), @(1700, 2600, "שתיים`nשורה שנייה", $false), @(2800, 3500, 'עוד לא מתוזמנת', $true))) {
    $c = $ctor.Invoke(@([long]$p[0], [long]$p[1], [string]$p[2])); $c.Untimed = $p[3]; $doc.Cues.Add($c)
}
$doc.Dirty = $true
$style = Fld $f '_style'; $style.FontPct = 8.5
$tl = Fld $f '_tl'
$tl.InPoint = 600; $tl.OutPoint = 3000
$tl.RestoreView(250.0, 400)
Call $f 'Seek' (Pack ([long]1234)) | Out-Null
Call $f 'SyncAfterDocChange' @() | Out-Null

Check 'ביציאה: שורות בלי תזמון -> להציע פרויקט' ((Call $f 'ExitSaveKind' @()) -eq 'project-new') (Call $f 'ExitSaveKind' @())

# גיבוי אוטומטי
Call $f 'AutoSave' @() | Out-Null
$pending = Call $f 'PendingRecovery' @()
Check 'גיבוי אוטומטי נכתב' ($pending -ne $null -and $pending.Cues.Count -eq 3) ''
Check 'הגיבוי נכתב לתיקיית הבדיקה' (Test-Path $testAuto) $testAuto

# שמירת פרויקט (בלי חלון: דרך הנתיב הפתוח)
$pp = [string](Join-Path $work 'עבודה #7.subtext')
$formT.GetField('_projectPath', $NP).SetValue($f, $pp)
$ok = Call $f 'SaveProject' (Pack $false)
Check 'שמירת פרויקט הצליחה' $ok ''
Check 'אחרי שמירה - לא מסומן כלא-שמור' (-not $doc.Dirty) ''
Check 'אחרי שמירה - הגיבוי נמחק (אחרת שחזור ״ישן מהשמור״)' ((Call $f 'PendingRecovery' @()) -eq $null) ''
$txt = [IO.File]::ReadAllText($pp)
Check 'הנתיב היחסי לסרט נשמר' ($txt.Contains('"relative": "שיעור #7.mp4"')) ''
$doc.Dirty = $true
Check 'ביציאה: פרויקט פתוח -> לשמור את הפרויקט' ((Call $f 'ExitSaveKind' @()) -eq 'project') ''
$f.Close(); $f.Dispose(); Pump 200

# SRT ליד הסרט - אסור שייטען לתוך פרויקט
[IO.File]::WriteAllText((Join-Path $work 'שיעור #7.srt'), "1`r`n00:00:00,100 --> 00:00:00,900`r`nזר`r`n", [Text.Encoding]::UTF8)

$g = NewForm
Call $g 'OpenProject' (Pack ([string]$pp)) | Out-Null
Pump 600
$doc2 = Fld $g '_doc'
$tl2 = Fld $g '_tl'
Check 'נפתח: שלוש כתוביות, בלי ה-SRT שליד הסרט' ($doc2.Cues.Count -eq 3) ("count=" + $doc2.Cues.Count)
Check 'נפתח: הטקסט עם שורה חדשה' ($doc2.Cues[1].Text -eq "שתיים`nשורה שנייה") ''
Check 'נפתח: השורה שלא תוזמנה עדיין לא מתוזמנת' ($doc2.Cues[2].Untimed) ''
Check 'נפתח: הסרט' ((Fld $g '_mediaPath') -eq $media) (Fld $g '_mediaPath')
Check 'נפתח: הפרויקט הוא הקובץ הפתוח' ((Fld $g '_projectPath') -eq $pp) ''
Check 'נפתח: לא מסומן כלא-שמור' (-not $doc2.Dirty) ''
Check 'נפתח: הטווח המסומן' ($tl2.InPoint -eq 600 -and $tl2.OutPoint -eq 3000) ("in=" + $tl2.InPoint + " out=" + $tl2.OutPoint)
Check 'נפתח: הזום' ($tl2.UserZoomed -and [Math]::Abs($tl2.PxPerSec - 250) -lt 0.01) ("px=" + $tl2.PxPerSec)
Check 'נפתח: המיקום' ([Math]::Abs($tl2.Position - 1234) -le 50) ("pos=" + $tl2.Position)
Check 'נפתח: העיצוב' ((Fld $g '_style').FontPct -eq 8.5) ''
Check 'נפתח: הפרויקט ראשון באחרונים, והסרט לא נוסף' (((T 'Settings').GetField('Recent', $ST).GetValue($null))[0] -eq $pp) ''
Check 'אחרי פתיחה - אין היסטוריית ביטול מהסשן הקודם' (-not $doc2.CanUndo) ''
$g.Close(); $g.Dispose(); Pump 200

# פתיחת SRT רגיל: עד 0.7.0 זה סימן ״יש שינויים״, והיציאה שאלה ״לשמור?״ על כלום
$srtOnly = Join-Path $work 'רק כתוביות.srt'
[IO.File]::WriteAllText($srtOnly, "1`r`n00:00:01,000 --> 00:00:02,000`r`nשלום`r`n", [Text.Encoding]::UTF8)
$q = NewForm
$formT.GetMethod('ImportSubs', $NP).Invoke($q, (Pack ([string]$srtOnly))) | Out-Null
$docQ = Fld $q '_doc'
Check 'פתיחת SRT - לא מסומן כלא-שמור' ($docQ.Cues.Count -eq 1 -and -not $docQ.Dirty) ("dirty=" + $docQ.Dirty)
Check 'ובלי שאלה ביציאה' ((Call $q 'ExitSaveKind' @()) -eq 'none') ''
$q.Close(); $q.Dispose(); Pump 100

# סרט שנמחק
Remove-Item $media -Force
$h = NewForm
Call $h 'OpenProject' (Pack ([string]$pp)) | Out-Null
Pump 200
Check 'סרט חסר: הכתוביות נפתחות בכל זאת' ((Fld $h '_doc').Cues.Count -eq 3) ''
Check 'סרט חסר: בלי סרט, בלי קריסה' ((Fld $h '_mediaPath') -eq $null -and (Fld $h '_mi') -eq $null) ''
Check 'סרט חסר: הפרויקט עדיין פתוח לשמירה' ((Fld $h '_projectPath') -eq $pp) ''
$h.Close(); $h.Dispose(); Pump 200

# שחזור מגיבוי
$k = NewForm
$docK = Fld $k '_doc'
$c = $ctor.Invoke(@([long]100, [long]900, [string]'לפני הקריסה')); $c.Untimed = $true; $docK.Cues.Add($c)
$docK.Dirty = $true
$formT.GetField('_projectPath', $NP).SetValue($k, $pp)
Call $k 'AutoSave' @() | Out-Null
# ״קריסה״: החלון נזרק בלי היציאה המסודרת שמוחקת את הגיבוי
$k.Dispose(); Pump 100
$m = NewForm
$pend = Call $m 'PendingRecovery' @()
Check 'אחרי קריסה - יש מה לשחזר' ($pend -ne $null -and $pend.Cues.Count -eq 1) ''
Call $m 'ApplyProject' (Pack $pend $null $true) | Out-Null
$docM = Fld $m '_doc'
Check 'שוחזר: הטקסט, ועדיין בלי תזמון' ($docM.Cues.Count -eq 1 -and $docM.Cues[0].Text -eq 'לפני הקריסה' -and $docM.Cues[0].Untimed) ''
Check 'שוחזר: **מסומן כלא-שמור** (הבאג של הגיבוי הישן)' ($docM.Dirty) ''
Check 'שוחזר: יודע לאיזה פרויקט הוא שייך' ((Fld $m '_projectPath') -eq $pp) ''
Check 'שוחזר: הגיבוי נשאר עד שמירה' ((Call $m 'PendingRecovery' @()) -ne $null) ''
Call $m 'ClearAutoSave' @() | Out-Null
Check 'אחרי ניקוי - אין שחזור' ((Call $m 'PendingRecovery' @()) -eq $null) ''
$docM.Dirty = $false
Check 'ביציאה: הכול שמור -> לא שואלים' ((Call $m 'ExitSaveKind' @()) -eq 'none') ''
$docM.Cues[0].Untimed = $false; $formT.GetField('_projectPath', $NP).SetValue($m, $null); $docM.Dirty = $true
Check 'ביציאה: כתוביות רגילות -> לשמור כתוביות' ((Call $m 'ExitSaveKind' @()) -eq 'subtitles') ''
$m.Close(); $m.Dispose()
$realAfter = if (Test-Path $realAuto) { (Get-Item $realAuto).LastWriteTimeUtc.Ticks } else { 0 }
Check 'הגיבוי האמיתי של המשתמש לא נגעו בו' ($realBefore -eq $realAfter) ''

# ================= 5. שיוך הסיומת במתקין =================
# לענף רישום משלנו, לא לשיוכים האמיתיים. דורש את המתקין הבנוי.
Write-Host 'שיוך .subtext במתקין'
$setupExe = Join-Path $root 'dist\SubtitleStudio-Setup.exe'
Check 'המתקין בנוי (make-installer.cmd)' (Test-Path $setupExe) $setupExe
if (Test-Path $setupExe) {
    $sasm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($setupExe))
    $assoc = $sasm.GetType('SubtitleStudioSetup.Assoc')
    $realExt = Test-Path 'HKCU:\Software\Classes\.subtext'
    $testRoot = 'Software\SubtextTest-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $classes = $testRoot + '\Classes'
    $fakeExe = 'C:\Program Files\אולפן #1\SubtitleStudio.exe'
    $assoc.GetMethod('Register', $ST).Invoke($null, @([string]$classes, [string]$fakeExe)) | Out-Null
    $hk = 'HKCU:\' + $classes
    Check 'הסיומת מפנה ל-Subtext.Project' ((Get-ItemProperty ($hk + '\.subtext')).'(default)' -eq 'Subtext.Project') ''
    $cmd = (Get-ItemProperty ($hk + '\Subtext.Project\shell\open\command')).'(default)'
    Check 'פקודת הפתיחה עם מרכאות סביב הנתיב ו-%1' ($cmd -eq ('"' + $fakeExe + '" "%1"')) $cmd
    Check 'אייקון' ((Get-ItemProperty ($hk + '\Subtext.Project\DefaultIcon')).'(default)' -eq ('"' + $fakeExe + '",0')) ''
    $assoc.GetMethod('Unregister', $ST).Invoke($null, @([string]$classes)) | Out-Null
    Check 'הסרה: לא נשאר כלום' ((-not (Test-Path ($hk + '\Subtext.Project'))) -and (-not (Test-Path ($hk + '\.subtext')))) ''
    # תוכנה אחרת לקחה את הסיומת אחרינו - ההסרה שלנו לא דורסת אותה
    $assoc.GetMethod('Register', $ST).Invoke($null, @([string]$classes, [string]$fakeExe)) | Out-Null
    Set-ItemProperty ($hk + '\.subtext') -Name '(default)' -Value 'Other.App'
    $assoc.GetMethod('Unregister', $ST).Invoke($null, @([string]$classes)) | Out-Null
    Check 'הסיומת של תוכנה אחרת נשארת' ((Get-ItemProperty ($hk + '\.subtext')).'(default)' -eq 'Other.App') ''
    Remove-Item ('HKCU:\' + $testRoot) -Recurse -Force -ErrorAction SilentlyContinue
    Check 'השיוכים האמיתיים לא נגעו בהם' ((Test-Path 'HKCU:\Software\Classes\.subtext') -eq $realExt) ''
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
