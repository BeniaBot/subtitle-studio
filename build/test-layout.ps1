# בדיקת פריסה בלי עין אנושית: בונה את החלון בכמה גדלים ומחפש פקדים שדורסים זה את זה
# או שגולשים מחוץ להורה. זה תופס בדיוק את מה שקשה לראות בצילום מסך.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

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

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
