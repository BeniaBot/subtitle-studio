# test-cut.ps1 - כלי החיתוך (0.8.7, בהשראת ״חותך שמע״): מקטעים לטווחים, לכתוביות ולקבצים.
# הלוגיקה (CutPlan) מול מקרים ידועים, ואחר כך המנוע עצמו על קבצים אמיתיים שנוצרים כאן: MP3, ‏M4A, ‏WAV
# (בלי קידוד מחדש, בפורמט המקורי) ו-MP4 (מהיר ומדויק) - ובודקים את האורך של מה שיצא.
# אחר כך החלון עצמו, עם הנגן האמיתי: גרירה, הקלדה, מקלדת, ניגון, וחיתוך דרך החלון.
# צפוי: 36 בדיקות.
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
# לפי שם, ובשם עם כמה עומסים - לפי מספר הפרמטרים (ברירת המחדל: הקצר)
function M($name, [int]$n = -1) { $best = $null; foreach ($m in $cp.GetMethods($ST)) { if ($m.Name -eq $name -and ($n -lt 0 -or $m.GetParameters().Count -eq $n)) { if ($best -eq $null -or $m.GetParameters().Count -lt $best.GetParameters().Count) { $best = $m } } }; if ($best -eq $null) { throw "no $name" }; return $best }
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

$x = (Secs @(,@(10000, 20000, $true)))[0]
[void]$se.Invoke($null, (Pack $x $true ([long]200000) ([long]94000)))
$y = (Secs @(,@(30000, 30000, $true)))[0]
[void]$se.Invoke($null, (Pack $y $false ([long]0) ([long]94000)))
Check 'התחלה אחרי סוף הקובץ - השנייה האחרונה; סוף בהתחלה של קטע ריק - השנייה הראשונה (לא קטע ריק)' ($x.A -eq 93000 -and $x.B -eq 94000 -and $y.A -eq 0 -and $y.B -eq 1000) ('' + $x.A + '-' + $x.B + ' / ' + $y.A + '-' + $y.B)

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

# ================= החלון =================
# החלון עם הנגן האמיתי של התוכנה, מחוץ למסך: גרירה על פס הקול, הקלדת זמנים, חצים, I/O, הכרטיסים,
# ״לשמוע רק את מה שיישמר״, ניגון קטע שעוצר בסופו - וחיתוך אמיתי דרך החלון (Plan/Finish), כולל מעבר לקובץ החתוך.
Write-Host 'החלון'
$mf = T 'MainForm'; $CD = T 'CutDlg'
function Fld($o, $n) { return $o.GetType().GetField($n, $IN).GetValue($o) }
function Pump([int]$ms) { $w = [Diagnostics.Stopwatch]::StartNew(); while ($w.ElapsedMilliseconds -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }
function Mouse($ctl, $what, [int]$x) { $e = New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left), 1, $x, 70, 0; [void][Windows.Forms.Control].GetMethod($what, $IN).Invoke($ctl, (Pack $e)) }
function Key($box, [Windows.Forms.Keys]$k) { [void][Windows.Forms.Control].GetMethod('OnKeyDown', $IN).Invoke($box, (Pack (New-Object Windows.Forms.KeyEventArgs $k))) }
function Cmd([Windows.Forms.Keys]$k) { $msg = [Windows.Forms.Message]::Create([IntPtr]::Zero, 0x100, [IntPtr]::Zero, [IntPtr]::Zero); $argv = New-Object object[] 2; $argv[0] = $msg; $argv[1] = $k; return $CD.GetMethod('ProcessCmdKey', $IN).Invoke($dlg, $argv) }
function Click($btn) { [void][Windows.Forms.Control].GetMethod('OnClick', $IN).Invoke($btn, (Pack ([EventArgs]::Empty))) }
function SeekP([long]$t) { [void]$CD.GetMethod('Seek', $IN).Invoke($dlg, @($t)) }
function PosP { return [long]$mf.GetProperty('PlayerPosition', $IN).GetValue($mainF, $null) }
function Playing { return [bool]$mf.GetProperty('PlayerIsPlaying', $IN).GetValue($mainF, $null) }
function Reset($spec) {
    $secs.Clear()
    foreach ($x in $spec) { [void]$CD.GetMethod('AddSection', $IN).Invoke($dlg, (Pack ([long]$x[0]) ([long]$x[1]) ([bool]$x[2]))) }
    if ($spec.Count -eq 0) { [void]$CD.GetMethod('RebuildRows', $IN).Invoke($dlg, @()) }
}
function Txt($l) { return (@($l) | ForEach-Object { '' + $_.A + '-' + $_.B + $(if ($_.Keep) { 'k' } else { 'r' }) }) -join ',' }

$mainF = [Activator]::CreateInstance($mf)
$mainF.StartPosition = 'Manual'; $mainF.Location = New-Object Drawing.Point -4000, -4000; $mainF.Size = New-Object Drawing.Size 1400, 900
$mainF.Show(); [Windows.Forms.Application]::DoEvents()
$srcV = Join-Path $dir 'src.mp4'
[void]$mf.GetMethod('OpenMedia', $IN).Invoke($mainF, @([string]$srcV))
$wave = $mf.GetField('_wave', $IN).GetValue($mainF)
$sw = [Diagnostics.Stopwatch]::StartNew(); while (-not $wave.Ready -and $sw.Elapsed.TotalSeconds -lt 60) { Pump 100 }
$mi = $mf.GetField('_mi', $IN).GetValue($mainF)
$doc = $mf.GetField('_doc', $IN).GetValue($mainF)
$D = [long]$mi.DurationMs
foreach ($c in @(@(11000, 13000, 'נחתכת בגבול'), @(15000, 17000, 'שלמה'), @(25000, 27000, 'בחלק שהוסר'), @(41000, 43000, 'בקטע השני'))) {
    [void]$doc.Cues.Add([Activator]::CreateInstance($cueT, @([long]$c[0], [long]$c[1], [string]$c[2])))
}
$secs = [Activator]::CreateInstance($listT)
$dlg = [Activator]::CreateInstance($CD, (Pack $mainF $mi $doc $wave $secs))
$dlg.StartPosition = 'Manual'; $dlg.Location = New-Object Drawing.Point -4000, -3000
$dlg.Show(); Pump 200
$view = Fld $dlg '_view'; $open = Fld $dlg '_open'; $split = Fld $dlg '_split'; $fast = Fld $dlg '_fast'; $prev = Fld $dlg '_preview'
Check 'חלון חדש: קטע אחד לשמירה, 0-30; ״לעבור לקובץ החתוך״ דלוק כי יש כתוביות; ״כל קטע לקובץ נפרד״ מוצג' ($secs.Count -eq 1 -and (Txt $secs) -eq '0-30000k' -and $open.Checked -and $open.Visible -and $split.Visible -and $wave.Ready) (Txt $secs)

$view.FitAll()
$x0 = [int]$view.X(40000); $x1 = [int]$view.X(50000)
Mouse $view 'OnMouseDown' $x0; Mouse $view 'OnMouseMove' ($x0 + 12); Mouse $view 'OnMouseMove' $x1; Mouse $view 'OnMouseUp' $x1
$px = [long]([Math]::Ceiling($view.MsPerPx)) + 1
$n = $secs[$secs.Count - 1]
Check 'גרירה על מקום ריק: קטע חדש מהנקודה שבה התחילה עד השחרור, מהסוג שבכרטיס' ($secs.Count -eq 2 -and [Math]::Abs($n.A - 40000) -le $px -and [Math]::Abs($n.B - 50000) -le $px -and $n.Keep) (Txt $secs)

$n.A = 40000; $n.B = 50000
Mouse $view 'OnMouseDown' ([int]$view.X(40000)); Mouse $view 'OnMouseMove' ([int]$view.X(45000))
$mid = $n.A
Mouse $view 'OnMouseMove' ([int]$view.X(53000)); Mouse $view 'OnMouseUp' ([int]$view.X(53000))
Check 'גרירת קו: ההתחלה זזה, וגרירה מעבר לסוף מחליפה ביניהם (בלי להיתקע)' ([Math]::Abs($mid - 45000) -le $px -and $n.A -eq 50000 -and [Math]::Abs($n.B - 53000) -le $px) ('mid=' + $mid + ' ' + (Txt @($n)))

Mouse $view 'OnMouseDown' ([int]$view.X(20000)); Mouse $view 'OnMouseUp' ([int]$view.X(20000))
Pump 150
Check 'לחיצה בלי גרירה: הנגן עובר לשם, ואין קטע חדש' ($secs.Count -eq 2 -and [Math]::Abs((PosP) - 20000) -le ($px + 50)) ('pos=' + (PosP))

$setMode = $CD.GetMethod('SetMode', $IN, $null, [Type[]]@([bool]), $null)
Reset @(@(10000, 20000, $true), @(40000, 50000, $true))
[void]$setMode.Invoke($dlg, @($false))
$rc = @($secs | Where-Object { (T 'CutPlan').GetField('RemoveColors', $ST).GetValue($null) -contains $_.Color }).Count
Pump 50
Check 'כרטיס ״להסיר״ כשכל הקטעים לשמירה: כולם מתהפכים, בצבעים של הסרה; ״כל קטע לקובץ נפרד״ נעלם' ((Txt $secs) -eq '10000-20000r,40000-50000r' -and $rc -eq 2 -and -not $split.Visible) (Txt $secs)
[void]$setMode.Invoke($dlg, @($true))
$rows = Fld $dlg '_rowCtl'
Click $rows[1].Kind
[void]$setMode.Invoke($dlg, @($false))
Check 'קטעים מעורבים: לחיצה על ״סוג״ הופכת רק את השורה, והכרטיס לא הופך אותם' ((Txt $secs) -eq '10000-20000k,40000-50000r' -and -not (Fld $dlg '_keepMode')) (Txt $secs)
[void]$setMode.Invoke($dlg, @($true))

$commit = $CD.GetMethod('Commit', $IN)
Reset @(,@(10000, 20000, $true))
$r0 = (Fld $dlg '_rowCtl')[0]; $s0 = $secs[0]
$r0.FA.Text = '0:12'; [void]$commit.Invoke($dlg, (Pack $r0.FA $s0 $true))
$a1 = $s0.A
$r0.FA.Text = 'abc'; [void]$commit.Invoke($dlg, (Pack $r0.FA $s0 $true))
$warn = (Fld $dlg '_summary').Color -eq (T 'Theme').GetProperty('Warn', $ST).GetValue($null, $null)
Check 'הקלדת התחלה: ״0:12״ נקבע; ״abc״ - הערך נשאר, התיבה חוזרת אליו, ואזהרה' ($a1 -eq 12000 -and $s0.A -eq 12000 -and $r0.FA.Text -eq '00:12.0' -and $warn) ('A=' + $s0.A + ' text=' + $r0.FA.Text)
$r0.FB.Text = '75'; [void]$commit.Invoke($dlg, (Pack $r0.FB $s0 $false))
$b1 = $s0.B
$r0.FA.Text = '1:30'; [void]$commit.Invoke($dlg, (Pack $r0.FA $s0 $true))
Check 'סוף אחרי סוף הקובץ - נקבע לסוף; התחלה אחרי סוף הקובץ - השנייה האחרונה, לא קטע ריק' ($b1 -eq $D -and $s0.B -eq $D -and $s0.A -eq $D - 1000) ('B=' + $b1 + ' then ' + (Txt @($s0)))

Reset @(,@(10000, 20000, $true))
$r0 = (Fld $dlg '_rowCtl')[0]; $s0 = $secs[0]
Key $r0.FA.Box ([Windows.Forms.Keys]::Up); $u1 = $s0.A
Key $r0.FA.Box ([Windows.Forms.Keys]::Up -bor [Windows.Forms.Keys]::Shift); $u2 = $s0.A
Key $r0.FB.Box ([Windows.Forms.Keys]::Down); $d1 = $s0.B
Check 'חצים בתיבת זמן: למעלה +שנייה, עם Shift +עשירית, למטה -שנייה; והתיבה מראה את מה שנשמר' ($u1 -eq 11000 -and $u2 -eq 11100 -and $d1 -eq 19000 -and $r0.FA.Text -eq '00:11.1') ("$u1 $u2 $d1 " + $r0.FA.Text)

SeekP 5000; Pump 60; [void](Cmd ([Windows.Forms.Keys]::I))
SeekP 25000; Pump 60; [void](Cmd ([Windows.Forms.Keys]::O))
Check 'I ו-O: התחלה וסוף של הקטע הפעיל איפה שהנגן עומד' ((Txt $secs) -eq '5000-25000k') (Txt $secs)
Reset @()
SeekP 30000; Pump 60; [void](Cmd ([Windows.Forms.Keys]::I))
SeekP 33000; Pump 60; [void](Cmd ([Windows.Forms.Keys]::O))
Check 'I בלי שום קטע: נפתח קטע חדש בנגן, ו-O סוגר אותו' ((Txt $secs) -eq '30000-33000k') (Txt $secs)

Reset @()
$p1 = $CD.GetMethod('Problem', $IN).Invoke($dlg, @())
$emptyShown = (Fld $dlg '_empty').Visible
Reset @(,@(0, $D, $false))
$p2 = $CD.GetMethod('Problem', $IN).Invoke($dlg, @())
Check 'אין קטעים - ״קודם מסמנים קטע״ (והשורה הריקה מסבירה); הכול מוסר - ״אין מה לשמור״' ($p1 -ne $null -and $p1[0] -eq 'קודם מסמנים קטע' -and $emptyShown -and $p2 -ne $null -and $p2[0] -eq 'אין מה לשמור') ''

# ---- חיתוך אמיתי דרך החלון ----
$planM = $CD.GetMethod('Plan', $IN); $finM = $CD.GetMethod('Finish', $IN)
function PlanTo([string]$target) { $argv = New-Object object[] 2; $argv[0] = $target; $r = $planM.Invoke($dlg, $argv); return @{ Run = $r; Problem = $argv[1] } }
$fast.Checked = $false
Reset @(@(40000, 50000, $true), @(10000, 20000, $true), @(12000, 14000, $false))
$split.Checked = $true; Pump 30
$sdir = Join-Path $dir 'split'; New-Item -ItemType Directory -Force $sdir | Out-Null
$pl = PlanTo $sdir
$names = @($pl.Run.Outs | ForEach-Object { [IO.Path]::GetFileName($_) })
$err = RunSteps $pl.Run.Job
$d1 = (Probe $pl.Run.Outs[0]).DurationSec; $d2 = (Probe $pl.Run.Outs[1]).DurationSec
[void]$finM.Invoke($dlg, @($pl.Run))
Check 'כל קטע לקובץ נפרד: הקובץ נקרא לפי המספר בטבלה (הראשון בזמן הוא ״קטע 2״), ומה שמוסר בתוכו יוצא' ($err -eq $null -and $names[0] -eq 'src - קטע 2.mp4' -and $names[1] -eq 'src - קטע 1.mp4' -and [Math]::Abs($d1 - 8) -lt 0.2 -and [Math]::Abs($d2 - 10) -lt 0.2) (($names -join ' | ') + "  $d1 / $d2  $err")
Check 'פיצול: הכתוביות לא זזות והעורך לא עובר קובץ' ($doc.Cues.Count -eq 4 -and (Fld $dlg 'OpenAfter') -eq $null -and -not $open.Visible) ''

$split.Checked = $false; Pump 30
$pl = PlanTo $srcV
Check 'שמירה על קובץ המקור נחסמת' ($pl.Run -eq $null -and $pl.Problem -ne $null -and $pl.Problem[0] -eq 'אותו קובץ') ''

$pl = PlanTo (Join-Path $dir 'ui-cut.mp4')
$err = RunSteps $pl.Run.Job
$du = (Probe $pl.Run.Outs[0]).DurationSec
[void]$finM.Invoke($dlg, @($pl.Run))
$got = (@($doc.Cues) | ForEach-Object { $_.Text + '@' + $_.Start + '-' + $_.End }) -join ' | '
Check 'חיתוך מדויק דרך החלון: 18 שניות, והכתוביות זזות לזמנים של הקובץ החדש' ($err -eq $null -and [Math]::Abs($du - 18) -lt 0.2 -and $got -eq 'נחתכת בגבול@1000-2000 | שלמה@3000-5000 | בקטע השני@9000-11000') ("dur=$du  $got  $err")

# ״לשמוע רק את מה שיישמר״: בניגון, רגע שמוסר - קפיצה לחלק הבא שנשאר
$prev.Checked = $true
Reset @(@(10000, 20000, $true), @(40000, 50000, $true))
SeekP 25000; Pump 60
[void]$mf.GetMethod('PlayPlayer', $IN).Invoke($mainF, @())
Pump 250
$pp = PosP
[void]$mf.GetMethod('PausePlayer', $IN).Invoke($mainF, @())
Check '״לשמוע רק את מה שיישמר״: הניגון מדלג על מה שמוסר' ($pp -ge 40000 -and $pp -lt 42000) ('pos=' + $pp)
$prev.Checked = $false

# ▶ בשורה: מנגן את הקטע ועוצר בסופו
Reset @(@(10000, 20000, $true), @(40000, 41000, $true))
Click (Fld $dlg '_rowCtl')[1].Play
$sw = [Diagnostics.Stopwatch]::StartNew(); while ((Playing) -and $sw.Elapsed.TotalSeconds -lt 6) { Pump 50 }
$pp = PosP
Check '▶ בשורה: מנגן מתחילת הקטע ועוצר בסופו בדיוק' (-not (Playing) -and $pp -eq 41000 -and $sw.Elapsed.TotalSeconds -gt 0.6) ('pos=' + $pp + ' after ' + [Math]::Round($sw.Elapsed.TotalSeconds, 2) + 's')

$after = Fld $dlg 'OpenAfter'
$dlg.Close(); $dlg.Dispose()
[void]$mf.GetMethod('SwitchToCut', $IN).Invoke($mainF, @([string]$after))
$mp = $mf.GetField('_mediaPath', $IN).GetValue($mainF)
$mi2 = $mf.GetField('_mi', $IN).GetValue($mainF)
Check 'אחרי החיתוך העורך עובר לקובץ החתוך, עם הכתוביות; השמירה הבאה תשאל לאן (לא דורסת את של המקור)' ($mp -eq $after -and [Math]::Abs($mi2.DurationSec - 18) -lt 0.2 -and $doc.Cues.Count -eq 3 -and $doc.FilePath -eq $null -and $doc.Dirty) ("media=" + [IO.Path]::GetFileName($mp) + " cues=" + $doc.Cues.Count)
$mainF.Close(); $mainF.Dispose()

Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
