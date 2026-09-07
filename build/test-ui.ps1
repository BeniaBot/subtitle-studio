# בדיקות ממשק בלי עכבר ובלי גניבת מיקוד.
#
# למה לא לחיצות אמיתיות: SetForegroundWindow נחסם כשהתהליך שמריץ אותו אינו
# בחזית, ואז הקליק נופל על חלון אחר בלי שום שגיאה - הבדיקה "עוברת" בשקר.
# כאן בונים את החלון בתוך התהליך, מחוץ למסך, ומפעילים את אותם מטפלי אירועים
# שהלחיצה הייתה מפעילה.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exe))
$formT = $asm.GetType('SubtitleStudio.MainForm')
$cueT  = $asm.GetType('SubtitleStudio.Cue')
$NP    = [Reflection.BindingFlags]'NonPublic,Instance'

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}" -f $name) }
    else { $script:fail++; Write-Host ("  FAIL {0}  {1}" -f $name, $detail) -ForegroundColor Red }
}
function Eq($name, $got, $want) { Check $name ($got -eq $want) ("got=$got want=$want") }
function Pack {
    $a = New-Object object[] $args.Count
    for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }
    return ,$a
}

function Fld($obj, $name) { return $formT.GetField($name, $NP).GetValue($obj) }
function Call($obj, $name, $argv) { return $formT.GetMethod($name, $NP).Invoke($obj, $argv) }

# הפעלת מטפל הלחיצה של פקד - בדיוק מה שלחיצה אמיתית הייתה עושה
function Tap($ctrl) {
    $m = $ctrl.GetType().GetMethod('OnClick', $NP)
    if (-not $m) { throw 'OnClick not found' }
    $m.Invoke($ctrl, (Pack ([EventArgs]::Empty))) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
}

function CloseExtraForms($keep) {
    for ($i = [System.Windows.Forms.Application]::OpenForms.Count - 1; $i -ge 0; $i--) {
        $w = [System.Windows.Forms.Application]::OpenForms[$i]
        if ($w -ne $keep) { $w.Close() }
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function NewForm($w, $h) {
    $f = [Activator]::CreateInstance($formT)
    $f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $f.Location = New-Object System.Drawing.Point -3000, -3000
    $f.ClientSize = New-Object System.Drawing.Size $w, $h
    $f.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $f.ClientSize = New-Object System.Drawing.Size $w, $h
    [System.Windows.Forms.Application]::DoEvents()
    return $f
}

function AddCue($doc, $a, $b, $text) {
    $list = $doc.GetType().GetField('Cues').GetValue($doc)
    $c = $cueT.GetConstructor([Type[]]@([long],[long],[string])).Invoke(@([long]$a,[long]$b,[string]$text))
    $list.Add($c)
    return $c
}

# ================= הפעולה הבסיסית: כתובית חדשה =================
Write-Host 'הכפתור הראשי'
$f = NewForm 1493 997
$doc = Fld $f '_doc'
$cues = $doc.GetType().GetField('Cues').GetValue($doc)
AddCue $doc 1000 3000 'ראשונה' | Out-Null
$doc.GetType().GetMethod('RaiseChanged').Invoke($doc, @()) | Out-Null
Call $f 'SyncAfterDocChange' (Pack) | Out-Null

$before = $cues.Count
Tap (Fld $f '_addCueBtn')
Eq 'כתובית חדשה נוספה' $cues.Count ($before + 1)

# הכתובית החדשה מתחילה במקום שבו הנגן עומד
$eng = Fld $f '_engine'
$pos = $eng.GetType().GetProperty('Position').GetValue($eng, $null)
$new = $null
foreach ($c in $cues) { if ($c.Text -eq '') { $new = $c } }
Check 'נוצרה במיקום הנגן' ($null -ne $new -and [Math]::Abs($new.Start - $pos) -lt 200) `
    ("start=" + $(if ($new) { $new.Start } else { 'null' }) + " pos=$pos")
Check 'הכתובית החדשה מסומנת' ($null -ne $new -and $new.Selected) ''

# ================= חלוניות קופצות =================
Write-Host 'חלוניות קופצות'
foreach ($pair in @(@('_volBtn', 'PopupPanel'), @('_speedBtn', 'PopupMenu'))) {
    CloseExtraForms $f
    $n = [System.Windows.Forms.Application]::OpenForms.Count
    Tap (Fld $f $pair[0])
    $opened = $null
    foreach ($w in [System.Windows.Forms.Application]::OpenForms) {
        if ($w -ne $f) { $opened = $w }
    }
    Check ($pair[0] + ' פותח ' + $pair[1]) ($null -ne $opened -and $opened.GetType().Name -eq $pair[1]) `
        ("opened=" + $(if ($opened) { $opened.GetType().Name } else { 'nothing' }))
}
CloseExtraForms $f

# הסליידר חוזר הביתה אחרי שהחלונית נסגרת, אחרת אי אפשר לפתוח שוב
$vol = Fld $f '_volume'
$card = Fld $f '_videoCard'
Check 'הסליידר חזר לכרטיס' ($vol.Parent -eq $card) ("parent=" + $(if ($vol.Parent) { $vol.Parent.GetType().Name } else { 'null' }))
Tap (Fld $f '_volBtn')
$again = $null
foreach ($w in [System.Windows.Forms.Application]::OpenForms) { if ($w -ne $f) { $again = $w } }
Check 'אפשר לפתוח את העוצמה שוב' ($null -ne $again) ''
CloseExtraForms $f

# ================= קיצורי מקלדת =================
Write-Host 'קיצורים'
$K = [System.Windows.Forms.Keys]
$eng.GetType().GetMethod('Seek').Invoke($eng, (Pack ([long]10000))) | Out-Null
Call $f 'HandleShortcut' (Pack ($K::Right -bor $K::Control)) | Out-Null
$p1 = $eng.GetType().GetProperty('Position').GetValue($eng, $null)
Eq 'Ctrl+ימינה קופץ שתי שניות' $p1 12000
Call $f 'HandleShortcut' (Pack ($K::Left -bor $K::Control -bor $K::Shift)) | Out-Null
$p2 = $eng.GetType().GetProperty('Position').GetValue($eng, $null)
Eq 'Ctrl+Shift+שמאלה קופץ חצי שנייה' $p2 11500

$sp0 = $eng.GetType().GetProperty('Speed').GetValue($eng, $null)
Call $f 'HandleShortcut' (Pack ($K::Down -bor $K::Control)) | Out-Null
$sp1 = $eng.GetType().GetProperty('Speed').GetValue($eng, $null)
Check 'Ctrl+למטה מאט' ($sp1 -lt $sp0) "from=$sp0 to=$sp1"
Call $f 'HandleShortcut' (Pack ($K::Up -bor $K::Control)) | Out-Null
$sp2 = $eng.GetType().GetProperty('Speed').GetValue($eng, $null)
Eq 'Ctrl+למעלה חוזר' $sp2 $sp0

# ================= כתובית ריקה לא נשמרת =================
Write-Host 'שמירה'
$tmp = Join-Path $env:TEMP ('ss_ui_' + [guid]::NewGuid().ToString('N').Substring(0,8) + '.srt')
$doc.GetType().GetField('FilePath').SetValue($doc, $tmp)
$n0 = $cues.Count
$ok = Call $f 'SaveSubtitles' (Pack $false)
Check 'השמירה הצליחה' $ok ''
Check 'הכתובית הריקה הושמטה' ($cues.Count -eq $n0 - 1) ("before=$n0 after=" + $cues.Count)
if (Test-Path $tmp) {
    $txt = [IO.File]::ReadAllText($tmp)
    Check 'הקובץ נכתב עם הכתובית האמיתית' ($txt -match 'ראשונה') ''
    Remove-Item $tmp -Force
} else { Check 'הקובץ נוצר' $false $tmp }

$f.Close(); $f.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ================= תזמון בלחיצה =================
Write-Host 'תזמון בלחיצה'
$g = NewForm 1493 997
$gdoc = Fld $g '_doc'
$gcues = $gdoc.GetType().GetField('Cues').GetValue($gdoc)

# טקסט חופשי עם שורות ריקות -> שלוש כתוביות בלי תזמון אמיתי
$fmt = $asm.GetType('SubtitleStudio.Formats')
$optT = $asm.GetType('SubtitleStudio.Formats+TextImportOptions')
$o = [Activator]::CreateInstance($optT)
$optT.GetField('SplitByBlankLine').SetValue($o, $true)
$optT.GetField('MarkUntimed').SetValue($o, $true)
$txt = "שלום לכולם`r`n`r`nזו בדיקה של התזמון`r`n`r`nוזו השורה השלישית"
$made = $fmt.GetMethod('ImportPlainText').Invoke($null, (Pack ([string]$txt) $o))
Eq 'הטקסט התחלק לשלוש' $made.Count 3
$allUntimed = $true
foreach ($c in $made) { if (-not $c.Untimed) { $allUntimed = $false } }
Check 'כולן מסומנות כלא-מתוזמנות' $allUntimed ''

foreach ($c in $made) { $gcues.Add($c) }
$gdoc.GetType().GetMethod('RaiseChanged').Invoke($gdoc, @()) | Out-Null

# מדמים סרט פתוח, כדי שהפס יופיע
$miT = $asm.GetType('SubtitleStudio.MediaInfo')
$mi = [Activator]::CreateInstance($miT)
$miT.GetField('DurationSec').SetValue($mi, [double]40)
$miT.GetField('Path').SetValue($mi, [string]'test.mp4')
$formT.GetField('_mi', $NP).SetValue($g, $mi)
Call $g 'UpdateTapUi' (Pack) | Out-Null
$bar = Fld $g '_tapBar'
Check 'הפס מופיע כשיש שורות בלי תזמון' $bar.Visible ''
Check 'הפס מציין כמה נשארו' ($bar.Text -like '*3*') $bar.Text

# נכנסים למצב ידנית (StartTapping מנגן, ואין כאן קובץ אמיתי)
$formT.GetField('_tapping', $NP).SetValue($g, $true)
$formT.GetField('_tapIndex', $NP).SetValue($g, [int]0)
Call $g 'UpdateTapUi' (Pack) | Out-Null
$add = Fld $g '_addCueBtn'
Check 'הכפתור הגדול מראה את השורה הבאה' ($add.Text -like '*שלום לכולם*') $add.Text

$geng = Fld $g '_engine'
function SeekTo($ms) { $geng.GetType().GetMethod('Seek').Invoke($geng, (Pack ([long]$ms))) | Out-Null }

SeekTo 2000
Call $g 'TapHere' (Pack) | Out-Null
Eq 'השורה הראשונה מתחילה בלחיצה' $gcues[0].Start 2000
Check 'וכבר לא מסומנת כהערכה' (-not $gcues[0].Untimed) ''
Eq 'עברנו לשורה הבאה' ($formT.GetField('_tapIndex', $NP).GetValue($g)) 1
Check 'הבאות נדחפו אחריה' ($gcues[1].Start -ge $gcues[0].End) ("next=" + $gcues[1].Start + " prevEnd=" + $gcues[0].End)

SeekTo 6000
Call $g 'TapHere' (Pack) | Out-Null
Eq 'השנייה מתחילה בלחיצה' $gcues[1].Start 6000
# הראשונה לא נשארת תלויה עד 6 שניות: נחתכת לפי אורך הטקסט + חסד
Check 'הראשונה לא נשארת תלויה בשקט' ($gcues[0].End -lt 5500 -and $gcues[0].End -gt 2500) $gcues[0].End

SeekTo 9000
Call $g 'TapHere' (Pack) | Out-Null
Eq 'השלישית מתחילה בלחיצה' $gcues[2].Start 9000
Check 'המצב נסגר לבד בסוף' (-not $formT.GetField('_tapping', $NP).GetValue($g)) ''
$left = 0
foreach ($c in $gcues) { if ($c.Untimed) { $left++ } }
Eq 'לא נשארו שורות בלי תזמון' $left 0

$g.Close(); $g.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ================= סרט בלי פס קול =================
# הבאג: Waveform.Ready נשאר false, הציר צייר "מכין את פס הקול" לנצח
# ורענן 30 פעמים בשנייה. הקובץ נוצר על ידי build\make-testmedia.ps1.
$silent = Join-Path $env:TEMP 'ss-gallery\silent.mp4'
if (Test-Path $silent) {
    Write-Host 'סרט בלי קול'
    $q = NewForm 1400 900
    Call $q 'OpenMedia' (Pack ([string]$silent)) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
    $mi2 = Fld $q '_mi'
    Check 'הקובץ נפתח' ($null -ne $mi2) ''
    if ($null -ne $mi2) {
        Check 'זוהה כחסר אודיו' (-not $mi2.HasAudio) ("HasAudio=" + $mi2.HasAudio)
        $wave = Fld $q '_wave'
        Check 'פס הקול מסומן כמוכן' ($wave.Ready) 'אחרת הציר מצייר בלי הפסקה'
    }
    $q.Close(); $q.Dispose()
    [System.Windows.Forms.Application]::DoEvents()
}

# ================= תוויות הסרגל =================
# הבאג שהיה: במסך צר כל התוויות ירדו בבת אחת ונשארו אייקונים בלי הסבר.
Write-Host 'תוויות הסרגל'
foreach ($w in @(1920, 1493, 1175)) {
    $g = NewForm $w 850
    $btns = Fld $g '_toolbarBtns'
    $named = 0
    foreach ($b in $btns) { if (-not $b.IconOnly) { $named++ } }
    # אינדקס 2 = "כתוביות", 3 = "הסרט" - שני התפריטים שאי אפשר לנחש לפי אייקון
    Check ("תפריטי הסרגל מכותרים ב-$w") ((-not $btns[2].IconOnly) -and (-not $btns[3].IconOnly)) `
        ("named=$named subs=" + $btns[2].IconOnly + " video=" + $btns[3].IconOnly)
    $g.Close(); $g.Dispose()
    [System.Windows.Forms.Application]::DoEvents()
}

Write-Host 'תפריט הכתוביות'
# הפריטים שדורשים סרט חייבים להיות כבויים בלי סרט, אחרת המשתמש לוחץ
# ומקבל הודעת שגיאה במקום כפתור מעומעם.
$g = NewForm 1493 900
$items = $formT.GetMethod('SubtitleMenuItems', $NP).Invoke($g, @())
function ItemNamed($list, $needle) {
    foreach ($it in $list) { if ($it.Text -and $it.Text.Contains($needle)) { return $it } }
    return $null
}
$tr = ItemNamed $items 'תמלול'
Check 'יש פריט תמלול בתפריט' ($null -ne $tr) ''
if ($tr) {
    Check 'התמלול כבוי בלי סרט' (-not $tr.Enabled) ("enabled=" + $tr.Enabled)
    Check 'התיאור מזכיר שהקול נשלח' ($tr.Desc -and $tr.Desc.Contains('גוגל')) ("desc=" + $tr.Desc)
}
$g.Close(); $g.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ועם סרט - הפריט נדלק
$g2 = NewForm 1493 900
$formT.GetField('_mi', $NP).SetValue($g2, $mi)
$items2 = $formT.GetMethod('SubtitleMenuItems', $NP).Invoke($g2, @())
$tr2 = ItemNamed $items2 'תמלול'
Check 'התמלול נדלק כשיש סרט' ($tr2 -and $tr2.Enabled) ''
$g2.Close(); $g2.Dispose()
[System.Windows.Forms.Application]::DoEvents()

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
