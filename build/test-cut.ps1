# test-cut.ps1 - כלי החיתוך (0.8.7, בהשראת ״חותך שמע״): מקטעים לטווחים, לכתוביות ולקבצים.
# הלוגיקה (CutPlan) מול מקרים ידועים, ואחר כך המנוע עצמו על קבצים אמיתיים שנוצרים כאן: MP3, ‏M4A, ‏WAV
# (בלי קידוד מחדש, בפורמט המקורי) ו-MP4 (מהיר ומדויק) - ובודקים את האורך של מה שיצא.
# צפוי: 16 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { return $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $v = $args[$i]; if ($null -ne $v) { $v = $v.psobject.BaseObject }; $a[$i] = $v }; return ,$a }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }
$cp = T 'CutPlan'
function M($name) { foreach ($m in $cp.GetMethods($ST)) { if ($m.Name -eq $name) { return $m } }; throw "no $name" }
$secT = T 'CutSection'
$listT = [Collections.Generic.List``1].MakeGenericType($secT)
function Secs($spec) {
    $l = [Activator]::CreateInstance($listT)
    $id = 1
    foreach ($s in $spec) {
        $x = [Activator]::CreateInstance($secT); $x.Id = $id++; $x.A = [long]$s[0]; $x.B = [long]$s[1]; $x.Keep = [bool]$s[2]
        [void]$l.Add($x)
    }
    return ,$l
}
function Rng($ranges) { return (@($ranges) | ForEach-Object { '' + $_[0] + '-' + $_[1] }) -join ',' }

# ================= הלוגיקה =================
Write-Host 'מה נשאר'
$s = Secs @(@(10000, 20000, $true), @(40000, 50000, $true), @(12000, 14000, $false))
$k = (M 'Kept').Invoke($null, (Pack $s ([long]94000)))
Check 'לשמור 10-20 ו-40-50, ולהסיר מתוכם 12-14' ((Rng $k) -eq '10000-12000,14000-20000,40000-50000') (Rng $k)
$s2 = Secs @(@(5000, 8000, $false), @(30000, 31000, $false))
Check 'רק להסיר: כל השאר נשמר' ((Rng ((M 'Kept').Invoke($null, (Pack $s2 ([long]60000))))) -eq '0-5000,8000-30000,31000-60000') ''
$s3 = Secs @(@(10000, 30000, $true), @(20000, 40000, $true))
Check 'קטעים חופפים מתאחדים' ((Rng ((M 'Kept').Invoke($null, (Pack $s3 ([long]60000))))) -eq '10000-40000') ''
$items = (M 'SplitItems').Invoke($null, (Pack $s))
Check 'קובץ לכל קטע לשמירה, פחות מה שמוסר בתוכו' ($items.Count -eq 2 -and (Rng $items[0]) -eq '10000-12000,14000-20000' -and (Rng $items[1]) -eq '40000-50000') ("files=" + $items.Count)
$ik = M 'IsKept'
Check 'רגע בתוך קטע להסרה - לא נשאר; בתוך קטע לשמירה - נשאר; מחוץ לכולם - לא' ((-not $ik.Invoke($null, (Pack $s ([long]13000)))) -and $ik.Invoke($null, (Pack $s ([long]15000))) -and (-not $ik.Invoke($null, (Pack $s ([long]30000))))) ''

Write-Host 'קביעת קצה'
$se = M 'SetEdge'
$x = (Secs @(,@(12000, 14000, $false)))[0]
[void]$se.Invoke($null, (Pack $x $true ([long]65000) ([long]94000)))
Check 'התחלה שהוקלדה אחרי הסוף: הקטע זז לשם ושומר על אורכו (ב״חותך שמע״ - 14-65, ואז אפס)' ($x.A -eq 65000 -and $x.B -eq 67000) ('' + $x.A + '-' + $x.B)
$x = (Secs @(,@(0, 30000, $true)))[0]
[void]$se.Invoke($null, (Pack $x $true ([long]42000) ([long]94000)))
[void]$se.Invoke($null, (Pack $x $false ([long]55000) ([long]94000)))
Check 'I אחרי סוף הקטע ואז O: הקטע מ-42 עד 55 (ב״חותך שמע״ נתקע ב-30)' ($x.A -eq 42000 -and $x.B -eq 55000) ('' + $x.A + '-' + $x.B)
$x = (Secs @(,@(50000, 60000, $true)))[0]
[void]$se.Invoke($null, (Pack $x $false ([long]20000) ([long]94000)))
Check 'סוף לפני ההתחלה: הקטע זז אחורה' ($x.A -eq 10000 -and $x.B -eq 20000) ('' + $x.A + '-' + $x.B)
$x = (Secs @(,@(80000, 90000, $true)))[0]
[void]$se.Invoke($null, (Pack $x $true ([long]92000) ([long]94000)))
Check 'קרוב לסוף הקובץ: לא עובר את הסוף' ($x.A -eq 92000 -and $x.B -eq 94000) ('' + $x.A + '-' + $x.B)

Write-Host 'כתוביות'
$cueT = T 'Cue'
$cl = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($cueT))
foreach ($c in @(@(11000, 13000, 'נחתכת בגבול'), @(15000, 17000, 'שלמה'), @(25000, 27000, 'בחלק שהוסר'), @(41000, 43000, 'בקטע השני'))) {
    [void]$cl.Add([Activator]::CreateInstance($cueT, @([long]$c[0], [long]$c[1], [string]$c[2])))
}
$mc = (M 'MapCues').Invoke($null, (Pack $cl $k))
$got = (@($mc) | ForEach-Object { $_.Text + '@' + $_.Start + '-' + $_.End }) -join ' | '
Check 'כתוביות אחרי חיתוך: זזות, נחתכות בגבול, ונעלמות מחלק שהוסר' ($got -eq 'נחתכת בגבול@1000-2000 | שלמה@3000-5000 | בקטע השני@9000-11000') $got

# ================= המנוע, על קבצים אמיתיים =================
Write-Host 'קבצים אמיתיים'
[void](T 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$ffx = [string](T 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
$dir = Join-Path $env:TEMP 'ss-cut'
New-Item -ItemType Directory -Force $dir | Out-Null
Get-ChildItem $dir -File | ForEach-Object { [IO.File]::Delete($_.FullName) }
& $ffx -nostdin -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=440:duration=60' -c:a libmp3lame -b:a 128k (Join-Path $dir 'src.mp3')
& $ffx -nostdin -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=440:duration=60' -c:a aac -b:a 128k (Join-Path $dir 'src.m4a')
& $ffx -nostdin -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=440:duration=60' -c:a pcm_s16le (Join-Path $dir 'src.wav')
& $ffx -nostdin -hide_banner -loglevel error -y -f lavfi -i 'testsrc=size=320x240:rate=25:duration=60' -f lavfi -i 'sine=frequency=440:duration=60' -c:v libx264 -g 50 -c:a aac -t 60 (Join-Path $dir 'src.mp4')
function Probe($p) { return (T 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$p)) }
function RunSteps($job) {
    foreach ($a in $job.Steps) {
        $argv = New-Object object[] 5; $argv[0] = $ffx; $argv[1] = '-nostdin -y ' + [string]$a; $argv[4] = [string]$job.WorkDir
        $code = (T 'Ff').GetMethod('RunSync', $ST).Invoke($null, $argv)
        if ($code -ne 0) { return ('exit ' + $code + ': ' + ([string]$argv[3]).Trim().Split("`n")[-1]) }
    }
    return $null
}
function Cut($src, $ranges, $fast, $outName) {
    $mi = Probe $src
    $rl = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType([long[]]))
    foreach ($r in $ranges) { [void]$rl.Add([long[]]@([long]$r[0], [long]$r[1])) }
    $out = (M 'FinalPath').Invoke($null, (Pack $mi (Join-Path $dir $outName) ([bool]$fast)))
    $items = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rl.GetType())); [void]$items.Add($rl)
    $outs = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType([string])); [void]$outs.Add([string]$out)
    $job = (M 'BuildJob').Invoke($null, (Pack $mi $items $outs ([bool]$fast)))
    $err = RunSteps $job
    $mo = if (Test-Path $out) { Probe $out } else { $null }
    return @{ Out = $out; Err = $err; Dur = $(if ($mo) { $mo.DurationSec } else { -1 }); Mi = $mo; Steps = $job.Steps.Length }
}
$three = @(@(10000, 12000), @(14000, 20000), @(40000, 50000))      # ‏18 שניות
foreach ($ext in 'mp3', 'm4a', 'wav') {
    $r = Cut (Join-Path $dir "src.$ext") $three $false "out.$ext"
    Check ("$ext - שלושה קטעים, בלי קידוד מחדש, באותו פורמט") ($r.Err -eq $null -and [IO.Path]::GetExtension($r.Out) -eq ".$ext" -and [Math]::Abs($r.Dur - 18) -lt 0.25 -and $r.Steps -eq 4) ("dur=" + $r.Dur + " steps=" + $r.Steps + " " + $r.Err)
}
$r = Cut (Join-Path $dir 'src.mp3') @(,@(5000, 35000)) $false 'one.mp3'
Check 'mp3 - קטע אחד: פקודה אחת, 30 שניות' ($r.Err -eq $null -and $r.Steps -eq 1 -and [Math]::Abs($r.Dur - 30) -lt 0.15) ("dur=" + $r.Dur)
$r = Cut (Join-Path $dir 'src.mp4') $three $true 'fast.mp4'
Check 'mp4 מהיר - חיבור בלי קידוד (מפריים מפתח: קצת יותר)' ($r.Err -eq $null -and $r.Dur -ge 17.8 -and $r.Dur -lt 22 -and $r.Mi.HasVideo -and $r.Mi.HasAudio) ("dur=" + $r.Dur + " " + $r.Err)
$r = Cut (Join-Path $dir 'src.mp4') $three $false 'exact.mp4'
Check 'mp4 מדויק - בדיוק 18 שניות, עם תמונה וקול' ($r.Err -eq $null -and [Math]::Abs($r.Dur - 18) -lt 0.15 -and $r.Mi.HasVideo -and $r.Mi.HasAudio -and $r.Steps -eq 1) ("dur=" + $r.Dur + " " + $r.Err)

Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
