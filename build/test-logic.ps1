# בדיקות לוגיקה ללא ממשק: טוען את ה-EXE כאסמבלי ומריץ את מנועי הזמן, הפורמטים והחישובים.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

$bytes = [System.IO.File]::ReadAllBytes($exe)
$asm = [System.Reflection.Assembly]::Load($bytes)
$T = { param($n) $asm.GetType("SubtitleStudio.$n") }

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}" -f $name) }
    else { $script:fail++; Write-Host ("  FAIL {0}  {1}" -f $name, $detail) -ForegroundColor Red }
}
function Eq($name, $got, $want) { Check $name ($got -eq $want) ("got=$got want=$want") }

# ---------- זמנים ----------
$tc = & $T 'Tc'
function TcCall($m, $v) { return $tc.GetMethod($m, [Type[]]@([long])).Invoke($null, @([long]$v)) }
function TcParse($s) { return $tc.GetMethod('Parse').Invoke($null, @([string]$s)) }

Write-Host 'זמנים'
Eq 'Srt(0)'            (TcCall 'Srt' 0)              '00:00:00,000'
Eq 'Srt(3661500)'      (TcCall 'Srt' 3661500)        '01:01:01,500'
Eq 'Vtt(3661500)'      (TcCall 'Vtt' 3661500)        '01:01:01.500'
Eq 'Ass(3661500)'      (TcCall 'Ass' 3661500)        '1:01:01.50'
Eq 'Parse srt'         (TcParse '01:01:01,500')      3661500
Eq 'Parse vtt'         (TcParse '01:01:01.500')      3661500
Eq 'Parse ass'         (TcParse '1:01:01.50')        3661500
Eq 'Parse mm:ss'       (TcParse '02:30')             150000
Eq 'Parse seconds'     (TcParse '12.5')              12500
Eq 'Parse junk'        (TcParse 'abc')               0
Eq 'roundtrip'         (TcParse (TcCall 'Srt' 7384123)) 7384123

# ---------- מסמך ----------
Write-Host 'מסמך וכתוביות'
$docT = & $T 'Doc'; $cueT = & $T 'Cue'
function NewDoc($triples) {
    $d = [Activator]::CreateInstance($docT)
    $list = $docT.GetField('Cues').GetValue($d)
    foreach ($t in $triples) {
        $c = $cueT.GetConstructor([Type[]]@([long],[long],[string])).Invoke(@([long]$t[0],[long]$t[1],[string]$t[2]))
        $list.Add($c)
    }
    return ,$d
}
function Cues($d) { return ,$docT.GetField('Cues').GetValue($d) }
function Call($obj, $m, $argv) { return $obj.GetType().GetMethod($m).Invoke($obj, $argv) }
# @(...) משטח אוספים לתוך המערך - חייבים לבנות object[] ידנית
function Pack {
    $a = New-Object object[] $args.Count
    for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }
    return ,$a
}

$d = NewDoc @(@(1000,3000,'א'), @(5000,7000,'ב'), @(9000,11000,'ג'))
$all = Cues $d
Call $d 'Shift' (Pack $all ([long]500)) | Out-Null
Eq 'Shift +0.5 start'  (Cues $d)[0].Start 1500
Eq 'Shift +0.5 end'    (Cues $d)[0].End   3500

Call $d 'Shift' (Pack $all ([long]-9000)) | Out-Null
Eq 'Shift clamps at 0' (Cues $d)[0].Start 0
Check 'Shift keeps duration' ((Cues $d)[0].End -gt 0) ((Cues $d)[0].End)

# מתיחה לינארית: 1s->2s ו-11s->21s  =>  6s->11.5s
$d = NewDoc @(@(1000,2000,'א'), @(6000,7000,'ב'), @(11000,12000,'ג'))
Call $d 'LinearSync' (Pack (Cues $d) ([long]1000) ([long]2000) ([long]11000) ([long]21000)) | Out-Null
Eq 'LinearSync A'      (Cues $d)[0].Start 2000
Eq 'LinearSync mid'    (Cues $d)[1].Start 11500
Eq 'LinearSync B'      (Cues $d)[2].Start 21000

# חפיפות
$d = NewDoc @(@(1000,5000,'א'), @(4000,6000,'ב'), @(9000,10000,'ג'))
Eq 'CountOverlaps'     (Call $d 'CountOverlaps' (Pack)) 1
$n = Call $d 'FixOverlaps' (Pack ([int]80))
Eq 'FixOverlaps count' $n 1
Eq 'FixOverlaps gap'   (Cues $d)[0].End 3920
Eq 'FixOverlaps after' (Call $d 'CountOverlaps' (Pack)) 0

# חיתוך טווח: שומרים 4s-8s => כתובית ב-5s עוברת ל-1s, וכתובית מחוץ לטווח נמחקת
$d = NewDoc @(@(1000,2000,'לפני'), @(5000,6000,'בפנים'), @(9000,10000,'אחרי'))
Call $d 'ApplyRangeEdit' (Pack ([long]4000) ([long]8000) $true) | Out-Null
Eq 'Trim keeps 1'      (Cues $d).Count 1
Eq 'Trim retimes'      (Cues $d)[0].Start 1000
Eq 'Trim text'         (Cues $d)[0].Text 'בפנים'

# ביטול וחזרה
$d = NewDoc @(@(1000,2000,'א'))
Call $d 'Push' (Pack ([string]'test')) | Out-Null
(Cues $d)[0].Start = 5000
Call $d 'Undo' (Pack) | Out-Null
Eq 'Undo'              (Cues $d)[0].Start 1000
Call $d 'Redo' (Pack) | Out-Null
Eq 'Redo'              (Cues $d)[0].Start 5000

# ---------- פורמטים ----------
Write-Host 'פורמטים'
$fmt = & $T 'Formats'
$tmp = Join-Path $env:TEMP ('ss_test_' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $tmp | Out-Null

$srt = "1`r`n00:00:01,000 --> 00:00:03,000`r`nשלום עולם`r`n`r`n2`r`n00:00:05,500 --> 00:00:09,000`r`nשורה ראשונה`r`nשורה שנייה`r`n"
$p1 = Join-Path $tmp 'a.srt'
[System.IO.File]::WriteAllText($p1, $srt, [System.Text.UTF8Encoding]::new($false))
$prT = & $T 'ParseResult'
$fmtT = & $T 'SubFormat'
function LoadCues($path) { return ,$prT.GetField('Cues').GetValue($fmt.GetMethod('Load').Invoke($null, (Pack ([string]$path)))) }
function LoadEnc($path)  { return $prT.GetField('Encoding').GetValue($fmt.GetMethod('Load').Invoke($null, (Pack ([string]$path)))) }
$lc = LoadCues $p1
Eq 'SRT count'         $lc.Count 2
Eq 'SRT start'         $lc[0].Start 1000
Eq 'SRT text'          $lc[0].Text 'שלום עולם'
Check 'SRT two lines'  ($lc[1].Text -match "`n") $lc[1].Text

# קידוד ישן: אותו תוכן ב-Windows-1255
$p2 = Join-Path $tmp 'ansi.srt'
[System.IO.File]::WriteAllText($p2, $srt, [System.Text.Encoding]::GetEncoding(1255))
$l2 = LoadCues $p2
Check 'CP1255 detected' ((LoadEnc $p2) -match '1255') (LoadEnc $p2)
Eq 'CP1255 text'       $l2[0].Text 'שלום עולם'

# הלוך ושוב בכל הפורמטים
$styleT = & $T 'SubStyle'
$style = [Activator]::CreateInstance($styleT)
foreach ($ext in @(@('srt','Srt'), @('vtt','Vtt'), @('ass','Ass'))) {
    $out = Join-Path $tmp ("rt." + $ext[0])
    $sf = [Enum]::Parse($fmtT, $ext[1])
    $fmt.GetMethod('Save').Invoke($null, (Pack ([string]$out) $lc $sf $style ([int]1920) ([int]1080) $true)) | Out-Null
    $back = LoadCues $out
    Eq ("roundtrip " + $ext[0] + " count") $back.Count 2
    Eq ("roundtrip " + $ext[0] + " start") $back[1].Start 5500
    Eq ("roundtrip " + $ext[0] + " text")  $back[0].Text 'שלום עולם'
}

# ---------- חישוב גודל יעד ----------
Write-Host 'חישוב גודל יעד'
$mtT = & $T 'MediaTools'; $ctxT = & $T 'ToolCtx'; $miT = & $T 'MediaInfo'
function Plan($durSec, $h, $srcMb, $targetMb) {
    $mi = [Activator]::CreateInstance($miT)
    $miT.GetField('DurationSec').SetValue($mi, [double]$durSec)
    $miT.GetField('Height').SetValue($mi, [int]$h)
    $miT.GetField('Width').SetValue($mi, [int]($h * 16 / 9))
    $miT.GetField('SizeBytes').SetValue($mi, [long]($srcMb * 1MB))
    $miT.GetField('HasAudio').SetValue($mi, $true)
    $miT.GetField('HasVideo').SetValue($mi, $true)
    $ctx = [Activator]::CreateInstance($ctxT)
    $ctxT.GetField('Mi').SetValue($ctx, $mi)
    $ctxT.GetField('Val').SetValue($ctx, [double]$targetMb)
    return ,$mtT.GetMethod('FitPlan').Invoke($null, (Pack $ctx))
}
function F($p, $name) { return $p.GetType().GetField($name).GetValue($p) }

# סרט של 10 דקות, 1080p, 600MB -> לרדת ל-200MB
$p = Plan 600 1080 600 200
$kb = F $p 'VideoKbps'
Check 'fit: bitrate sane' ($kb -gt 1500 -and $kb -lt 2800) $kb
Check 'fit: not flagged small' (-not (F $p 'AlreadySmall')) ''
# הגודל המתקבל בפועל קרוב ליעד
$mb = ($kb + (F $p 'AudioKbps')) * 600 / 8 / 1024.0
Check 'fit: lands near target' ($mb -gt 180 -and $mb -le 200) ([math]::Round($mb,1))

# קובץ שכבר קטן מהיעד
$p2 = Plan 600 720 80 200
Check 'fit: already small' (F $p2 'AlreadySmall') ''

# יעד קטן מדי -> אזהרה + הורדת רזולוציה
$p3 = Plan 3600 1080 4000 50
Check 'fit: too small flag' (F $p3 'TooSmall') ''
Check 'fit: downscales' ((F $p3 'Height') -gt 0 -and (F $p3 'Height') -le 480) (F $p3 'Height')

# סרט קצר ואיכותי - לא מנפחים מעבר למקור
$p4 = Plan 60 1080 30 200
Check 'fit: caps at source' ((F $p4 'VideoKbps') -lt 5200) (F $p4 'VideoKbps')

# ---------- השוואת גרסאות לעדכון ----------
Write-Host 'מנגנון עדכונים'
$appT = & $T 'App'
$cur = $appT.GetField('Version').GetValue($null)
function Newer($v) { return $appT.GetMethod('IsNewer').Invoke($null, (Pack ([string]$v))) }
Check 'version format'   ($cur -match '^\d+\.\d+\.\d+$') $cur
Check 'same is not newer' (-not (Newer $cur)) $cur
Check 'v-prefix handled'  (-not (Newer ("v" + $cur))) ''
Check 'patch newer'       (Newer '0.1.1') ''
Check 'minor newer'       (Newer '0.2.0') ''
Check 'major newer'       (Newer '1.0.0') ''
Check 'older rejected'    (-not (Newer '0.0.9')) ''
Check 'beta tag parsed'   (Newer 'v0.2.0-beta') ''
Check 'garbage rejected'  (-not (Newer 'not-a-version')) ''
Check 'empty rejected'    (-not (Newer '')) ''

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail) -ForegroundColor $(if ($fail) { 'Red' } else { 'Green' })
if ($fail) { exit 1 }
