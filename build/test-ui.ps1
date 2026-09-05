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

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
