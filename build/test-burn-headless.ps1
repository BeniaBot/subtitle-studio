# צריבת עברית מקצה לקצה, בלי ממשק ובלי עכבר.
#
# זו הפונקציה המרכזית של התוכנה, והיא נשענת על שרשרת עדינה: ASS בשם קצר
# באנגלית ב-%TEMP%\SubStudio, ffmpeg שמורץ עם WorkingDirectory שם, ובילד
# עם libass+fribidi+harfbuzz. אם משהו בשרשרת נשבר, העברית יוצאת הפוכה או
# ריקה - ואי אפשר לדעת את זה מקומפילציה.
#
#   powershell -ExecutionPolicy Bypass -File build\test-burn-headless.ps1
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

Add-Type -AssemblyName System.Drawing
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }; return ,$a }

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}" -f $name) }
    else { $script:fail++; Write-Host ("  FAIL {0}  {1}" -f $name, $detail) -ForegroundColor Red }
}

$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $media)) {
    Write-Host 'no test media - run build\make-testmedia.ps1 first'
    exit 1
}

# ---------- מנוע ----------
# חובה לפרוס קודם את המנוע המוטמע. בלי זה Ff.Locate נופל אחורה ל-PATH
# ותופס ffmpeg אקראי שמותקן במחשב - וכל הבדיקה הזאת הייתה מאשרת בנייה
# של מישהו אחר במקום את שלנו.
$rtT = T 'Runtime'
$rtT.GetMethod('Prepare', $ST).Invoke($null, @())
$deployed = $rtT.GetProperty('FfmpegPath', $ST).GetValue($null, $null)
Check 'המנוע המוטמע נפרס' ($deployed -and (Test-Path $deployed)) ("FfmpegPath=" + $deployed)

$ff = (T 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
Check 'נמצא מנוע ffmpeg' ($ff -and (Test-Path $ff)) $ff
if (-not $ff) { exit 1 }
# השורה שמונעת מהבדיקה לרמות אותנו שוב
Check 'הבדיקה רצה על המנוע שלנו' ($ff -eq $deployed) ("בפועל: " + $ff)

$cfg = (& $ff -hide_banner -version 2>&1) -join ' '
Check 'הבילד כולל libass'     ($cfg -match 'enable-libass') ''
Check 'הבילד כולל libfribidi' ($cfg -match 'enable-libfribidi') ''

# ---------- כתוביות בעברית ----------
$docT = T 'Doc'; $cueT = T 'Cue'; $styleT = T 'SubStyle'
$listT = [System.Collections.Generic.List``1].MakeGenericType($cueT)
$cues = [Activator]::CreateInstance($listT)
foreach ($c in @(@(500, 3500, 'שלום לכולם'), @(4000, 7000, 'זו בדיקת צריבה'))) {
    $cue = $cueT.GetConstructor([Type[]]@([long],[long],[string])).Invoke(@([long]$c[0], [long]$c[1], [string]$c[2]))
    $listT.GetMethod('Add').Invoke($cues, (Pack $cue))
}
$style = [Activator]::CreateInstance($styleT)

$mi = (T 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, (Pack ([string]$media)))
Check 'הסרט נקרא' ($null -ne $mi) ''

# ---------- אותו קוד שהתוכנה מריצה ----------
$burnT = T 'Burn'
$dirArgs = New-Object object[] 6
$dirArgs[0] = $cues; $dirArgs[1] = $style
$dirArgs[2] = [int]$mi.Width; $dirArgs[3] = [int]$mi.Height
$dirArgs[4] = [long]0; $dirArgs[5] = $null
$ass = $burnT.GetMethod('WriteTempAss', $ST).Invoke($null, $dirArgs)
$workDir = $dirArgs[5]
Check 'נכתב קובץ ASS' ($ass -and (Test-Path (Join-Path $workDir $ass))) ("ass=$ass dir=$workDir")
Check 'שם הקובץ באנגלית בלבד' ($ass -match '^[\x20-\x7E]+$') $ass

$assText = [IO.File]::ReadAllText((Join-Path $workDir $ass), [Text.Encoding]::UTF8)
Check 'הטקסט העברי נשמר ב-ASS' ($assText -match 'שלום לכולם') ''

# ---------- צריבה בפועל ----------
$out = Join-Path $env:TEMP ('ss-burn-' + [guid]::NewGuid().ToString('N').Substring(0,6) + '.mp4')
$args = '-y -hide_banner -v error -t 6 -i "' + $media + '" -vf "subtitles=' + $ass + '" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -an "' + $out + '"'
$psi = New-Object Diagnostics.ProcessStartInfo $ff, $args
$psi.UseShellExecute = $false
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$psi.WorkingDirectory = $workDir
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = [Diagnostics.Process]::Start($psi)
$errText = $p.StandardError.ReadToEnd()
$p.WaitForExit(120000) | Out-Null
Check 'הצריבה הסתיימה בהצלחה' ($p.ExitCode -eq 0) ("exit=" + $p.ExitCode + " " + $errText.Substring(0, [Math]::Min(200, $errText.Length)))
Check 'נוצר קובץ פלט' ((Test-Path $out) -and (Get-Item $out).Length -gt 20000) ''
Write-Host ("       (" + $sw.ElapsedMilliseconds + " ms)")

# ---------- הפריים באמת מכיל פיקסלים לבנים במקום הכתובית ----------
if (Test-Path $out) {
    $frame = Join-Path $env:TEMP ('ss-frame-' + [guid]::NewGuid().ToString('N').Substring(0,6) + '.png')
    & $ff -y -hide_banner -v error -ss 2 -i $out -frames:v 1 $frame 2>$null | Out-Null
    if (Test-Path $frame) {
        $bmp = [System.Drawing.Bitmap]::FromFile($frame)
        # הכתובית יושבת בשליש התחתון; סופרים פיקסלים בהירים שם
        $white = 0
        $y0 = [int]($bmp.Height * 0.72); $y1 = [int]($bmp.Height * 0.95)
        for ($y = $y0; $y -lt $y1; $y += 2) {
            for ($x = 0; $x -lt $bmp.Width; $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 235 -and $c.G -gt 235 -and $c.B -gt 235) { $white++ }
            }
        }
        $bmp.Dispose()
        Check 'הכתובית נראית על הפריים' ($white -gt 150) ("bright pixels=$white")
        Remove-Item $frame -Force -ErrorAction SilentlyContinue
    } else { Check 'חולץ פריים לבדיקה' $false '' }
    Remove-Item $out -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
