# איפה נוחתת הנקודה: מדידה על הפריים עצמו, לא על המחרוזת.
#
# סימן פיסוק בסוף משפט הוא תו **ניטרלי** ב-UAX#9. הצד שאליו הוא נופל
# נקבע לפי כיוון הפסקה, ולא לפי האותיות שלידו. אי אפשר לדעת מהקוד מה
# libass החליט - צורבים, ומודדים איפה הדיו.
#
# הזיהוי: הכתובית ממורכזת, ולכן הוספת תו מרחיבה את התיבה לשני הכיוונים
# ואי אפשר להסיק מזה כלום. במקום זה מסתכלים על **גובה** הדיו בקצוות:
# נקודה היא כתם נמוך בלבד, ואות עברית מגיעה עד הראש. הצד שבו הדיו נמוך
# הוא הצד שבו הנקודה.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){$a[$i]=$args[$i]}; return ,$a }

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

(T 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$ff = (T 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
if (-not $ff) { Write-Host 'no ffmpeg'; exit 1 }

$cueT = T 'Cue'; $styleT = T 'SubStyle'; $burnT = T 'Burn'
$listT = [System.Collections.Generic.List``1].MakeGenericType($cueT)
$ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
$style = [Activator]::CreateInstance($styleT)
$W = 1280; $H = 720

# מצייר כתובית אחת ומחזיר את מפת הדיו
function Render($text) {
    $cues = [Activator]::CreateInstance($listT)
    [void]$listT.GetMethod('Add').Invoke($cues, (Pack ($ctor.Invoke(@([long]0, [long]4000, [string]$text)))))
    $a = New-Object object[] 6
    $a[0]=$cues; $a[1]=$style; $a[2]=[int]$W; $a[3]=[int]$H; $a[4]=[long]0; $a[5]=$null
    $ass = $burnT.GetMethod('WriteTempAss', $ST).Invoke($null, $a)
    $dir = $a[5]
    $png = Join-Path $env:TEMP ('bidi-' + [guid]::NewGuid().ToString('N').Substring(0,8) + '.png')
    $ffArgs = '-y -hide_banner -v error -f lavfi -i color=c=black:s=' + $W + 'x' + $H +
              ':d=1 -vf "subtitles=' + $ass + '" -frames:v 1 "' + $png + '"'
    $psi = New-Object Diagnostics.ProcessStartInfo $ff, $ffArgs
    $psi.UseShellExecute=$false; $psi.RedirectStandardError=$true; $psi.CreateNoWindow=$true
    $psi.WorkingDirectory = $dir
    $p = [Diagnostics.Process]::Start($psi)
    $e = $p.StandardError.ReadToEnd(); $p.WaitForExit(60000) | Out-Null
    if (-not (Test-Path $png)) { Write-Host ("   render failed: " + $e); return $null }
    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    $y0 = [int]($H*0.60); $y1 = [int]($H*0.99)
    $cols = @{}
    $lo = 99999; $hi = -1
    for ($y = $y0; $y -lt $y1; $y++) {
        for ($x = 0; $x -lt $W; $x++) {
            $c = $bmp.GetPixel($x,$y)
            if ($c.R -gt 120 -or $c.G -gt 120 -or $c.B -gt 120) {
                if (-not $cols.ContainsKey($x)) { $cols[$x] = @($y, $y) }
                else { $v = $cols[$x]; if ($y -lt $v[0]) { $v[0] = $y }; if ($y -gt $v[1]) { $v[1] = $y }; $cols[$x] = $v }
                if ($x -lt $lo) { $lo = $x }
                if ($x -gt $hi) { $hi = $x }
            }
        }
    }
    $bmp.Dispose(); Remove-Item $png -Force -ErrorAction SilentlyContinue
    if ($hi -lt 0) { Write-Host ("   no ink for [" + $text + "]"); return $null }
    return @{ Lo=$lo; Hi=$hi; Cols=$cols }
}

# הראש הגבוה ביותר של הדיו ב-N העמודות שבקצה
function TopAt($r, $fromLeft, $n) {
    $best = 99999
    for ($k = 0; $k -lt $n; $k++) {
        $x = if ($fromLeft) { $r.Lo + $k } else { $r.Hi - $k }
        if ($r.Cols.ContainsKey($x)) { $t = $r.Cols[$x][0]; if ($t -lt $best) { $best = $t } }
    }
    return $best
}

# מחזיר 'left' / 'right' - איפה יושב הכתם הנמוך (הנקודה)
function DotSide($text, $width) {
    $r = Render $text
    if (-not $r) { return 'render-failed' }
    $topAll = 99999
    foreach ($k in $r.Cols.Keys) { $t = $r.Cols[$k][0]; if ($t -lt $topAll) { $topAll = $t } }
    $botAll = 0
    foreach ($k in $r.Cols.Keys) { $b = $r.Cols[$k][1]; if ($b -gt $botAll) { $botAll = $b } }
    $h = $botAll - $topAll
    $tl = TopAt $r $true  $width
    $tr = TopAt $r $false $width
    Write-Host ("        [$text]  " + $r.Lo + '..' + $r.Hi + "  גובה=$h  ראש-שמאל=" +
                ($tl - $topAll) + "  ראש-ימין=" + ($tr - $topAll)) -ForegroundColor DarkGray
    # הצד שבו הדיו מתחיל **נמוך** (רחוק מהראש) הוא הצד של הנקודה
    if (($tl - $topAll) -gt $h * 0.5 -and ($tl - $topAll) -gt ($tr - $topAll)) { return 'left' }
    if (($tr - $topAll) -gt $h * 0.5 -and ($tr - $topAll) -gt ($tl - $topAll)) { return 'right' }
    return 'unclear'
}

Write-Host 'לאן נופלת הנקודה הסופית'
foreach ($c in @(
    @('שלום לכולם.',                    'נקודה אחרי עברית'),
    @('זו בדיקה של תזמון.',             'משפט ארוך יותר'),
    @('3 דברים שכדאי לדעת.',            'שורה שמתחילה בספרה'),
    @('שלום לכולם,',                    'פסיק'),
    @('נתראה מחר.',                     'משפט קצר')
)) {
    $s = DotSide $c[0] 6
    Check $c[1] ($s -eq 'left') ("הנקודה נחתה: " + $s)
}

Write-Host ''
Write-Host 'ערוץ כתוביות רך - שם היה הבאג בפועל'
# הצריבה עוברת דרך libass, שעושה זיהוי כיוון פסקה אוטומטי ולכן
# הסתדרה לבד. ערוץ רך נמסר לנגן, והנגן קובע בסיס LTR - שם הנקודה
# ברחה ימינה. הבדיקה היא על הקובץ שנמסר, לא על הפריים.
$RLM = [char]0x200F
$fmtT = $asm.GetType('SubtitleStudio.SubFormat')
$cues2 = [Activator]::CreateInstance($listT)
[void]$listT.GetMethod('Add').Invoke($cues2, (Pack ($ctor.Invoke(@([long]0, [long]2000, [string]'שלום לכולם.')))))
$aa = New-Object object[] 6
$aa[0]=$cues2; $aa[1]=[Enum]::Parse($fmtT,'Srt'); $aa[2]=$style
$aa[3]=[int]$W; $aa[4]=[int]$H; $aa[5]=$null
$muxName = (T 'Burn').GetMethod('WriteTempSubs', $ST).Invoke($null, $aa)
$muxText = [IO.File]::ReadAllText((Join-Path $aa[5] $muxName), [Text.Encoding]::UTF8)
# שורה שפותחת באות עברית לא צריכה עוגן פתיחה - האות עצמה היא העוגן.
# עוגן מיותר הוא תו נוסף בכל שורה בלי שום תמורה.
Check 'עוגן סגירה נוסף אחרי הנקודה' ($muxText.Contains('.' + $RLM)) ''
Check 'ובלי עוגן פתיחה מיותר'       (-not $muxText.Contains($RLM + 'ש')) ''

# ושורה שפותחת בספרה צריכה את שניהם
$cues3 = [Activator]::CreateInstance($listT)
[void]$listT.GetMethod('Add').Invoke($cues3, (Pack ($ctor.Invoke(@([long]0, [long]2000, [string]'3 דברים שכדאי לדעת.')))))
$ab = New-Object object[] 6
$ab[0]=$cues3; $ab[1]=[Enum]::Parse($fmtT,'Srt'); $ab[2]=$style
$ab[3]=[int]$W; $ab[4]=[int]$H; $ab[5]=$null
$n2 = (T 'Burn').GetMethod('WriteTempSubs', $ST).Invoke($null, $ab)
$t2 = [IO.File]::ReadAllText((Join-Path $ab[5] $n2), [Text.Encoding]::UTF8)
Check 'שורה שפותחת בספרה מעוגנת משני הצדדים' (($t2.Contains($RLM + '3')) -and ($t2.Contains('.' + $RLM))) ''
# ומה שהמשתמש שומר חייב להישאר נקי
$plain = (T 'Formats').GetMethod('ToSrt', $ST).Invoke($null, (Pack $cues2))
Check 'הקובץ של המשתמש נשאר בלי RLM' ($plain.IndexOf($RLM) -lt 0) ''

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
