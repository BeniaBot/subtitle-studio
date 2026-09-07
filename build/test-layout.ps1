# בדיקת פריסה בלי עין אנושית: בונה את החלון בכמה גדלים ומחפש פקדים שדורסים זה את זה
# או שגולשים מחוץ להורה. זה תופס בדיוק את מה שקשה לראות בצילום מסך.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

$env:SUBSTUDIO_TEST = '1'   # בלי דיאלוגים ובלי פנייה לרשת בזמן בדיקה
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exe))
$formT = $asm.GetType('SubtitleStudio.MainForm')
$themeT = $asm.GetType('SubtitleStudio.Theme')

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}" -f $name) }
    else { $script:fail++; Write-Host ("  FAIL {0}  {1}" -f $name, $detail) -ForegroundColor Red }
}

# חפיפות מכוונות: הרמז שמצויר מעל תיבת טקסט ריקה, וכפתור ניקוי הסימון שיושב על הציר.
# HeroPanel = מסך הפתיחה שמכסה את מסך העבודה; רק אחד מהם מוצג בפועל.
$allowOverlap = @('Lbl<->TextBox', 'TimelineControl<->Btn', 'Btn<->TimelineControl',
                  'HeroPanel<->Card', 'Card<->HeroPanel')

function Walk($c, $path, [ref]$problems) {
    $kids = @()
    foreach ($k in $c.Controls) { if ($k.Visible) { $kids += $k } }
    for ($i = 0; $i -lt $kids.Count; $i++) {
        $a = $kids[$i]
        if ($a.Width -le 0 -or $a.Height -le 0) { continue }
        for ($j = $i + 1; $j -lt $kids.Count; $j++) {
            $b = $kids[$j]
            if ($b.Width -le 0 -or $b.Height -le 0) { continue }
            $ra = New-Object System.Drawing.Rectangle $a.Left, $a.Top, $a.Width, $a.Height
            $rb = New-Object System.Drawing.Rectangle $b.Left, $b.Top, $b.Width, $b.Height
            $hit = [System.Drawing.Rectangle]::Intersect($ra, $rb)
            if ($hit.Width -gt 2 -and $hit.Height -gt 2) {
                $na = $a.GetType().Name; $nb = $b.GetType().Name
                if ($allowOverlap -contains ($na + '<->' + $nb)) { continue }
                $problems.Value += ("{0}: {1}{2} <-> {3}{4}  ({5}x{6})" -f $path, $na, $ra, $nb, $rb, $hit.Width, $hit.Height)
            }
        }
        Walk $a ($path + '/' + $a.GetType().Name) $problems
    }
}

foreach ($size in @(@(940, 680), @(1175, 850), @(1493, 997), @(1920, 1080))) {
    $w = $size[0]; $h = $size[1]
    $f = [Activator]::CreateInstance($formT)
    $f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $f.Location = New-Object System.Drawing.Point -3000, -3000   # מחוץ למסך, בלי להפריע למשתמש
    $f.ClientSize = New-Object System.Drawing.Size $w, $h
    $f.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $f.ClientSize = New-Object System.Drawing.Size $w, $h
    [System.Windows.Forms.Application]::DoEvents()

    $problems = @()
    Walk $f 'form' ([ref]$problems)

    # פקדים שגולשים מחוץ להורה
    $spill = @()
    function Spill($c, $path, [ref]$out) {
        # ScrollHost מחזיק ילדים מתחת לאזור הנראה בכוונה - זו בדיוק המשמעות של גלילה
        if ($c.GetType().Name -eq 'ScrollHost') { return }
        foreach ($k in $c.Controls) {
            if (-not $k.Visible) { continue }
            if ($k.Width -le 0 -or $k.Height -le 0) { continue }
            if ($k.Left -lt -2 -or $k.Top -lt -2 -or $k.Right -gt $c.ClientSize.Width + 2 -or $k.Bottom -gt $c.ClientSize.Height + 2) {
                $out.Value += ("{0}/{1} at {2},{3} {4}x{5} in {6}x{7}" -f $path, $k.GetType().Name, $k.Left, $k.Top, $k.Width, $k.Height, $c.ClientSize.Width, $c.ClientSize.Height)
            }
            Spill $k ($path + '/' + $k.GetType().Name) $out
        }
    }
    Spill $f 'form' ([ref]$spill)

    $f.Close()
    $f.Dispose()
    [System.Windows.Forms.Application]::DoEvents()

    Check ("no overlap at " + $w + "x" + $h) ($problems.Count -eq 0) ("`n     " + ($problems -join "`n     "))
    Check ("no spill at " + $w + "x" + $h)   ($spill.Count -eq 0)    ("`n     " + ($spill -join "`n     "))
}

# ---------- חלוניות קופצות ----------
# בלי לבנות MainForm (יקר ואיטי): בודקים שהחלונית עצמה נפתחת ומארחת פקד,
# ושהמתודות שמפעילות אותה קיימות.
$ppT = $asm.GetType('SubtitleStudio.PopupPanel')
$slT = $asm.GetType('SubtitleStudio.Slider')
Check 'PopupPanel exists' ($null -ne $ppT) ''
if ($ppT) {
    $sl = [Activator]::CreateInstance($slT)
    $pp = $ppT.GetConstructors()[0].Invoke(@($sl, [int]210, [int]58))
    $pp.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $pp.Location = New-Object System.Drawing.Point -3000, -3000
    $pp.Show()
    [System.Windows.Forms.Application]::DoEvents()
    Check 'PopupPanel shows'       $pp.Visible ''
    Check 'PopupPanel hosts child' ($pp.Controls.Count -eq 1) $pp.Controls.Count
    Check 'PopupPanel sizes child' ($sl.Width -gt 100 -and $sl.Height -gt 10) ("{0}x{1}" -f $sl.Width, $sl.Height)
    $pp.Close(); $pp.Dispose()
    [System.Windows.Forms.Application]::DoEvents()
}
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
foreach ($m in @('ShowVolume', 'ShowSpeedMenu', 'ShowCueMenuAt')) {
    Check ("$m exists") ($null -ne $formT.GetMethod($m, $flags)) ''
}

# ---------- מצב כהה ----------
# אחרי החלפת ערכה אסור שיישאר פקד עם רקע בהיר. זה בדיוק הבאג שהיה.
$f = [Activator]::CreateInstance($formT)
$f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$f.Location = New-Object System.Drawing.Point -3000, -3000
$f.ClientSize = New-Object System.Drawing.Size 1493, 997
$f.Show()
[System.Windows.Forms.Application]::DoEvents()

$dark = $themeT.GetField('Dark').GetValue($null)
if (-not $dark) {
    $formT.GetMethod('ToggleTheme', [Reflection.BindingFlags]'NonPublic,Instance').Invoke($f, @()) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
}
Check 'theme is dark' ($themeT.GetField('Dark').GetValue($null)) ''

$light = @()
function Lum($c) { return (0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B) / 255.0 }
function Scan($c, $path, [ref]$out) {
    foreach ($k in $c.Controls) {
        if (-not $k.Visible) { continue }
        if ($k.Width -lt 8 -or $k.Height -lt 8) { continue }
        if ((Lum $k.BackColor) -gt 0.72) {
            $out.Value += ("{0}/{1} back={2} {3}x{4}" -f $path, $k.GetType().Name, $k.BackColor.Name, $k.Width, $k.Height)
        }
        Scan $k ($path + '/' + $k.GetType().Name) $out
    }
}
Scan $f 'form' ([ref]$light)
Check 'no light controls in dark mode' ($light.Count -eq 0) ("`n     " + ($light -join "`n     "))

# חזרה למצב בהיר, כדי שהבדיקה לא תשנה את ההעדפה של המשתמש
$formT.GetMethod('ToggleTheme', [Reflection.BindingFlags]'NonPublic,Instance').Invoke($f, @()) | Out-Null
[System.Windows.Forms.Application]::DoEvents()
$f.Close(); $f.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ---------- מסך הפתיחה ----------
# HeroPanel מצייר את עצמו: אין פקדי בן, אז חפיפות לא רלוונטיות.
# מה שכן אפשר לשבור זה הגאומטריה - אזור הגרירה שגולש, שלבים שנחתכים,
# או שורת קובץ אחרון שיוצאת מהמסך. וגם: שהציור עצמו לא זורק חריגה.
$heroT = $asm.GetType('SubtitleStudio.HeroPanel')
$HP = [Reflection.BindingFlags]'NonPublic,Instance'
function HeroGet($h, $name) {
    $pi = $heroT.GetProperty($name, $HP)
    if ($pi) { return $pi.GetValue($h, $null) }
    $mi = $heroT.GetMethod($name, $HP)
    if ($mi) { return $mi.Invoke($h, @()) }
    return $null
}

foreach ($size in @(@(940, 680), @(1175, 850), @(1493, 997))) {
    $w = $size[0]; $h = $size[1]
    $hero = [Activator]::CreateInstance($heroT)
    $hero.SetBounds(0, 0, $w, $h)
    $hero.GetType().GetMethod('Reposition').Invoke($hero, @()) | Out-Null

    # ציור אמיתי לביטמפ - GDI+ קרס כאן בעבר על מלבן ברוחב אפס
    $painted = $true
    try {
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $hero.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, $w, $h))
        $bmp.Dispose()
    } catch { $painted = $false; $paintErr = $_.Exception.Message }
    Check ("מסך הפתיחה מצויר ב-${w}x${h}") $painted $paintErr

    $zone = HeroGet $hero 'Zone'
    $stepsTop = HeroGet $hero 'StepsTop'
    $stepsH   = HeroGet $hero 'StepsH'
    $blockTop = HeroGet $hero 'BlockTop'
    $rc       = HeroGet $hero 'RecentCount'
    $showRec  = HeroGet $hero 'ShowRecent'

    Check ("אזור הגרירה בתוך המסך ב-${w}x${h}") `
        ($zone.X -ge 0 -and $zone.Y -ge 0 -and ($zone.X + $zone.Width) -le $w -and ($zone.Y + $zone.Height) -le $h) `
        ("zone=" + $zone.ToString() + " in ${w}x${h}")
    Check ("שלושת השלבים נכנסים ב-${w}x${h}") (($stepsTop + $stepsH) -le $h) `
        ("stepsBottom=" + ($stepsTop + $stepsH) + " h=$h")
    Check ("הבלוק לא נחתך מלמעלה ב-${w}x${h}") ($blockTop -ge 0) ("blockTop=$blockTop")

    if ($showRec -and $rc -gt 0) {
        $last = $heroT.GetMethod('RecentRow', $HP).Invoke($hero, @([int]($rc - 1)))
        Check ("הקבצים האחרונים נכנסים ב-${w}x${h}") (($last.Y + $last.Height) -le $h) `
            ("bottom=" + ($last.Y + $last.Height) + " h=$h")
    }

    $hero.Dispose()
}

# ---------- דיאלוגים ----------
# הם רוב הפקדים בתוכנה, ובהם קל להחמיץ כפתור שגלש מהחלון אחרי הוספת שורה.
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }

# מסמך ומדיה לדוגמה
$doc = NewOf 'Doc' @()
$dcues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
foreach ($c in @(@(1000,4000,'שלום לכולם'), @(5500,9000,'This is a test line'), @(11000,15000,'הכתובית השלישית'))) {
    $dcues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2])))
}
$style = NewOf 'SubStyle' @()
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
$mi = $null
if (Test-Path $media) {
    try { $mi = (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$media)) } catch { }
}
function ToolNamed($like) {
    $all = (TY 'MediaTools').GetMethod('All', $ST).Invoke($null, @())
    foreach ($x in $all) { if ((TY 'MediaTool').GetField('Name', $IN).GetValue($x) -like $like) { return $x } }
    return $all[0]
}

$dialogs = @(
    @{ n = 'ImportText'; needsMedia = $false; make = { NewOf 'ImportTextDlg' @([int64]0) } },
    @{ n = 'Transcribe'; needsMedia = $true;  make = { NewOf 'TranscribeDlg' @($mi, [int]3) } },
    @{ n = 'Fix';        needsMedia = $false; make = { NewOf 'FixDlg' @($doc) } },
    @{ n = 'Sync';       needsMedia = $false; make = { NewOf 'SyncDlg' @($doc, [int64]7900, (NewOf 'SyncState' @())) } },
    @{ n = 'Help';       needsMedia = $false; make = { NewOf 'HelpDlg' @() } },
    @{ n = 'About';      needsMedia = $false; make = { NewOf 'AboutDlg' @() } },
    @{ n = 'AiSetup';    needsMedia = $false; make = { NewOf 'AiSetupDlg' @() } },
    @{ n = 'AiTranslate';needsMedia = $false; make = { NewOf 'AiTranslateDlg' @($doc) } },
    @{ n = 'Trim';       needsMedia = $true;  make = { NewOf 'TrimDlg' @($null, $mi, $doc, [int64]5000, [int64]15000) } },
    @{ n = 'Extract';    needsMedia = $true;  make = { NewOf 'ExtractSubsDlg' @($null, $mi) } },
    @{ n = 'Tools';      needsMedia = $true;  make = { NewOf 'ToolsDlg' @($null, $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Export';     needsMedia = $true;  make = { NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [int64]-1, [int64]-1) } },
    @{ n = 'FitSize';    needsMedia = $true;  make = { NewOf 'ToolRunDlg' @($null, (ToolNamed '*גודל קובץ*'), $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Reverse';    needsMedia = $true;  make = { NewOf 'ToolRunDlg' @($null, (ToolNamed '*ריוורס*'), $mi, [int64]-1, [int64]-1, [int64]0) } }
)

$screenH = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Height
foreach ($d in $dialogs) {
    if ($d.needsMedia -and $null -eq $mi) { continue }
    $dlg = $null
    try { $dlg = & $d.make } catch { Check ("דיאלוג " + $d.n) $false $_.Exception.InnerException.Message; continue }
    $dlg.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $dlg.Location = New-Object System.Drawing.Point -3000, -3000
    $dlg.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $probs = @(); $sp = @()
    Walk $dlg ('dlg:' + $d.n) ([ref]$probs)
    Spill $dlg ('dlg:' + $d.n) ([ref]$sp)
    $tall = $dlg.Height -gt $screenH
    $dlg.Close(); $dlg.Dispose()
    [System.Windows.Forms.Application]::DoEvents()

    Check ($d.n + ": בלי חפיפות") ($probs.Count -eq 0) ("`n     " + ($probs -join "`n     "))
    Check ($d.n + ": בלי גלישה")  ($sp.Count -eq 0)    ("`n     " + ($sp -join "`n     "))
    Check ($d.n + ": נכנס למסך")  (-not $tall)         ("height=" + $dlg.Height + " screen=" + $screenH)
}

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
