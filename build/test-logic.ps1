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
$pv = [version]$cur
Check 'patch newer'       (Newer ("{0}.{1}.{2}" -f $pv.Major, $pv.Minor, ($pv.Build + 1))) ''
Check 'minor newer'       (Newer ("{0}.{1}.0" -f $pv.Major, ($pv.Minor + 1))) ''
Check 'major newer'       (Newer ("{0}.0.0" -f ($pv.Major + 1))) ''
Check 'older rejected'    (-not (Newer '0.0.9')) ''
Check 'beta tag parsed'   (Newer ("v{0}.{1}.0-beta" -f $pv.Major, ($pv.Minor + 1))) ''
Check 'garbage rejected'  (-not (Newer 'not-a-version')) ''
Check 'empty rejected'    (-not (Newer '')) ''

# ---------- ניתוח פלט ffmpeg ----------
Write-Host "זיהוי קבצי מדיה"
$ffT = & $T "Ff"
$BF = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
$parse = $ffT.GetMethod("ParseFfmpegInfo", $BF)
$miT2 = & $T "MediaInfo"

function Probe($text) {
    $mi = [Activator]::CreateInstance($miT2)
    [void]$parse.Invoke($null, (Pack ([string]$text) $mi))
    return ,$mi
}
function MiF($mi, $name) { return $miT2.GetField($name).GetValue($mi) }

# קובץ רגיל
$t1 = @"
Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'x.mp4':
  Duration: 00:12:34.56, start: 0.000000, bitrate: 1500 kb/s
  Stream #0:0(und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1920x1080 [SAR 1:1 DAR 16:9], 1400 kb/s, 25 fps, 25 tbr, 90k tbn
  Stream #0:1(und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s
"@
$m1 = Probe $t1
Eq 'probe: duration'  ([math]::Round((MiF $m1 'DurationSec'), 2)) 754.56
$streamT = & $T "MediaStream"
function StF($st, $n) { return $streamT.GetField($n).GetValue($st) }
function Vid($mi) {
    foreach ($st in (MiF $mi 'Streams')) { if ((StF $st 'Type') -eq 'video') { return ,$st } }
    return $null
}
$v1 = Vid $m1
Eq 'probe: width'     (StF $v1 'Width')  1920
Eq 'probe: height'    (StF $v1 'Height') 1080
Eq 'probe: fps'       (StF $v1 'Fps')    25

# וידאו מסובב 90 מעלות - הציר האמיתי מתהפך
$t2 = @"
Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'phone.mp4':
  Duration: 00:00:30.00, start: 0.000000, bitrate: 9000 kb/s
  Stream #0:0(und): Video: h264 (High), yuv420p, 1920x1080, 8900 kb/s, 30 fps, 30 tbr, 90k tbn
    Side data:
      displaymatrix: rotation of -90.00 degrees
  Stream #0:1(und): Audio: aac (LC), 44100 Hz, mono, fltp, 96 kb/s
"@
$m2 = Probe $t2
$v2 = Vid $m2
Eq 'rotated: rotation read' (StF $v2 'Rotation') -90
Check 'rotated: raw is landscape' ((StF $v2 'Width') -gt (StF $v2 'Height')) ("{0}x{1}" -f (StF $v2 'Width'), (StF $v2 'Height'))

# שני ערוצי שמע וכתוביות מוטמעות
$t3 = @"
Input #0, matroska,webm, from 'movie.mkv':
  Duration: 01:45:00.00, start: 0.000000, bitrate: 4000 kb/s
  Stream #0:0: Video: h264 (High), yuv420p, 1280x720, 24 fps, 24 tbr, 1k tbn
  Stream #0:1(heb): Audio: ac3, 48000 Hz, 5.1(side), fltp, 448 kb/s
  Stream #0:2(eng): Audio: aac (LC), 48000 Hz, stereo, fltp, 128 kb/s
  Stream #0:3(heb): Subtitle: subrip
  Stream #0:4(eng): Subtitle: hdmv_pgs_subtitle
"@
$m3 = Probe $t3
$subs = $miT2.GetMethod("Subtitles").Invoke($m3, $null)
Eq 'mkv: two subtitle tracks' $subs.Count 2
$streams = MiF $m3 'Streams'
Eq 'mkv: five streams' $streams.Count 5
Eq 'mkv: duration hours' ([math]::Round((MiF $m3 'DurationSec') / 60, 0)) 105

# משך לא ידוע
$t4 = @"
Input #0, mp3, from 'stream.mp3':
  Duration: N/A, start: 0.000000, bitrate: 128 kb/s
  Stream #0:0: Audio: mp3, 44100 Hz, stereo, fltp, 128 kb/s
"@
$m4 = Probe $t4
Eq 'unknown duration is zero' (MiF $m4 'DurationSec') 0

# קצב פריימים עשרוני
$t5 = @"
Input #0, mov,mp4, from 'ntsc.mp4':
  Duration: 00:00:10.00, start: 0.000000, bitrate: 500 kb/s
  Stream #0:0: Video: h264, yuv420p, 640x480, 400 kb/s, 29.97 fps, 29.97 tbr, 30k tbn
"@
$m5 = Probe $t5
$v5 = Vid $m5
Check 'ntsc fps' ([math]::Abs((StF $v5 'Fps') - 29.97) -lt 0.02) (StF $v5 'Fps')

# ---------- קבצים מלוכלכים ----------
Write-Host "קבצים חריגים"

function WriteSrt($name, $text, $enc) {
    $path = Join-Path $tmp $name
    [IO.File]::WriteAllText($path, $text, $enc)
    return $path
}
$utf8bom = New-Object Text.UTF8Encoding($true)
$utf8 = New-Object Text.UTF8Encoding($false)

# 1. BOM + CRLF + בלי שורה ריקה בסוף
$p1 = WriteSrt "bom.srt" "1`r`n00:00:01,000 --> 00:00:03,000`r`nעם BOM" $utf8bom
$c1 = LoadCues $p1
Eq 'BOM: count' $c1.Count 1
Eq 'BOM: text'  $c1[0].Text "עם BOM"

# 2. שורות בלי הפרדה ריקה בין הקטעים
$p2 = WriteSrt "nogap.srt" "1`n00:00:01,000 --> 00:00:02,000`nראשון`n2`n00:00:03,000 --> 00:00:04,000`nשני`n" $utf8
$c2 = LoadCues $p2
Eq 'no blank line: count' $c2.Count 2
Eq 'no blank line: second' $c2[1].Text "שני"

# 3. מספור שבור ולא ממוין
$p3 = WriteSrt "unordered.srt" "7`n00:00:05,000 --> 00:00:06,000`nמאוחר`n`n3`n00:00:01,000 --> 00:00:02,000`nמוקדם`n" $utf8
$c3 = LoadCues $p3
Eq 'unordered: count' $c3.Count 2
$d3 = [Activator]::CreateInstance($docT)
$l3 = $docT.GetField('Cues').GetValue($d3)
foreach ($c in $c3) { $l3.Add($c) }
Call $d3 'Sort' (Pack) | Out-Null
Eq 'unordered: sorted first' (Cues $d3)[0].Text "מוקדם"

# 4. תגיות עיצוב ושתי שורות
$p4 = WriteSrt "tags.srt" "1`n00:00:01,000 --> 00:00:04,000`n<i>מוטה</i> ו<b>מודגש</b>`nשורה שנייה`n" $utf8
$c4 = LoadCues $p4
Eq 'tags: count' $c4.Count 1
Check 'tags: two lines kept' ($c4[0].Text -match "`n") $c4[0].Text
Check 'tags: plain text strips markup' ($c4[0].PlainText -notmatch '<') $c4[0].PlainText

# 5. זמנים עם נקודה ורווחים חריגים בחץ
$p5 = WriteSrt "loose.srt" "1`n00:00:01.500-->00:00:03.250`nזמנים חריגים`n" $utf8
$c5 = LoadCues $p5
Eq 'loose times: count' $c5.Count 1
Eq 'loose times: start' $c5[0].Start 1500
Eq 'loose times: end'   $c5[0].End 3250

# 6. כתובית ריקה באמצע
$p6 = WriteSrt "empty.srt" "1`n00:00:01,000 --> 00:00:02,000`n`n`n2`n00:00:03,000 --> 00:00:04,000`nיש טקסט`n" $utf8
$c6 = LoadCues $p6
Check 'empty cue: at least the real one' ($c6.Count -ge 1) $c6.Count

# 7. קובץ ריק לגמרי
$p7 = WriteSrt "blank.srt" "" $utf8
$c7 = LoadCues $p7
Eq 'blank file' $c7.Count 0

# 8. זמן סיום לפני התחלה
$p8 = WriteSrt "reversed.srt" "1`n00:00:09,000 --> 00:00:04,000`nהפוך`n" $utf8
$c8 = LoadCues $p8
Check 'reversed times survive load' ($c8.Count -eq 1) $c8.Count
Check 'reversed times not negative duration' ($c8[0].End -ge $c8[0].Start) ("{0}..{1}" -f $c8[0].Start, $c8[0].End)

# ---------- עומס: אלפי כתוביות ----------
Write-Host "עומס"
$big = Join-Path $tmp "big.srt"
$sb = New-Object Text.StringBuilder
for ($i = 1; $i -le 3000; $i++) {
    $t0 = [TimeSpan]::FromMilliseconds(($i - 1) * 1200)
    $t1 = [TimeSpan]::FromMilliseconds(($i - 1) * 1200 + 1400)   # חפיפה מכוונת עם הבאה
    [void]$sb.AppendLine($i)
    [void]$sb.AppendLine(("{0:hh\:mm\:ss\,fff} --> {1:hh\:mm\:ss\,fff}" -f $t0, $t1))
    [void]$sb.AppendLine("שורה $i - טקסט לבדיקת עומס עם מילים ארוכות")
    [void]$sb.AppendLine("")
}
[IO.File]::WriteAllText($big, $sb.ToString(), [Text.UTF8Encoding]::new($false))

$sw = [Diagnostics.Stopwatch]::StartNew()
$bigCues = LoadCues $big
$loadMs = $sw.ElapsedMilliseconds
Eq 'load 3000 cues' $bigCues.Count 3000
Check 'load under 2s' ($loadMs -lt 2000) "${loadMs}ms"

$bigDoc = [Activator]::CreateInstance($docT)
$bigList = $docT.GetField('Cues').GetValue($bigDoc)
foreach ($c in $bigCues) { $bigList.Add($c) }

$sw.Restart()
$overlaps = Call $bigDoc 'CountOverlaps' (Pack)
$cntMs = $sw.ElapsedMilliseconds
Eq 'overlaps found' $overlaps 2999
Check 'count under 300ms' ($cntMs -lt 300) "${cntMs}ms"

$sw.Restart()
$fixedN = Call $bigDoc 'FixOverlaps' (Pack ([int]80))
$fixMs = $sw.ElapsedMilliseconds
Eq 'fixed all overlaps' (Call $bigDoc 'CountOverlaps' (Pack)) 0
Check 'fix under 500ms' ($fixMs -lt 500) "${fixMs}ms"

$sw.Restart()
Call $bigDoc 'Shift' (Pack (Cues $bigDoc) ([long]1500)) | Out-Null
$shiftMs = $sw.ElapsedMilliseconds
Eq 'shift kept count' (Cues $bigDoc).Count 3000
Check 'shift under 200ms' ($shiftMs -lt 200) "${shiftMs}ms"

$bigOut = Join-Path $tmp "big-out.srt"
$sw.Restart()
$fmt.GetMethod('Save').Invoke($null, (Pack ([string]$bigOut) (Cues $bigDoc) ([Enum]::Parse($fmtT, 'Srt')) $style ([int]1920) ([int]1080) $true)) | Out-Null
$saveMs = $sw.ElapsedMilliseconds
Check 'save under 1.5s' ($saveMs -lt 1500) "${saveMs}ms"
$back = LoadCues $bigOut
Eq 'roundtrip 3000' $back.Count 3000
Eq 'roundtrip text' $back[2999].Text (Cues $bigDoc)[2999].Text

# ביטול אחרי פעולה כבדה
$before = (Cues $bigDoc)[0].Start
Call $bigDoc 'Push' (Pack ([string]'test')) | Out-Null
Call $bigDoc 'Shift' (Pack (Cues $bigDoc) ([long]5000)) | Out-Null
Call $bigDoc 'Undo' (Pack) | Out-Null
Eq 'undo on 3000 cues' (Cues $bigDoc)[0].Start $before

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
Write-Host ''

# ---------- רישיונות מוטמעים ----------
# GPLv3 דורש שנוסח הרישיון ילווה את הבינארי. אם מישהו ימחק שורת /resource
# מ-build.cmd, ההפצה תיהפך ללא-תואמת בלי שאף אחד ישים לב.
Write-Host 'רישיונות'
$want = @(
    @('MIT.txt',      'MIT License'),
    @('GPL-3.0.txt',  'GNU GENERAL PUBLIC LICENSE'),
    @('OFL-1.1.txt',  'SIL OPEN FONT LICENSE')
)
foreach ($w in $want) {
    $st = $asm.GetManifestResourceStream($w[0])
    if ($null -eq $st) { Check ("מוטמע: " + $w[0]) $false 'resource missing' ; continue }
    $sr = New-Object System.IO.StreamReader $st
    $txt = $sr.ReadToEnd(); $sr.Close()
    Check ("מוטמע: " + $w[0]) ($txt -like ('*' + $w[1] + '*')) ("len=" + $txt.Length)
}
# הגופן עצמו - אם הוא בפנים, הרישיון שלו חייב להיות בפנים
Check 'גופן מוטמע יחד עם הרישיון שלו' `
    (($null -eq $asm.GetManifestResourceStream('Assistant-Regular.ttf')) -or `
     ($null -ne $asm.GetManifestResourceStream('OFL-1.1.txt'))) ''

# ---------- סקריפט העדכון ----------
# הבאג שהיה: הסקריפט נכתב ב-CP1255 ו-cmd קורא OEM, אז כל נתיב עם עברית
# יצא ג'יבריש והתוכנה פשוט לא חזרה אחרי עדכון.
Write-Host 'סקריפט העדכון'
$upT = & $T 'Updater'
function MkScript($a, $b) { return $upT.GetMethod('UpdateScript').Invoke($null, (Pack ([string]$a) ([string]$b))) }
function IsAscii($t) { foreach ($ch in $t.ToCharArray()) { if ([int]$ch -gt 126) { return $false } } return $true }

$script = MkScript (Join-Path $env:TEMP 'SubtitleStudio-9.9.9.exe') (Join-Path $env:LOCALAPPDATA 'Programs\SubtitleStudio\SubtitleStudio.exe')
Check 'הסקריפט יוצא ASCII נקי' (IsAscii $script) ($script.Substring(0, [Math]::Min(100, $script.Length)))
Check 'יש תקרה לניסיונות'      ($script -match 'geq 15') ''
Check 'מפעיל מחדש בכל מקרה'    ($script -match 'start ') ''
Check 'מוחק את עצמו'           ($script -match 'del "%~f0"') ''

# נתיב עם עברית - בדיוק המקרה שנפל
$heb = Join-Path $env:APPDATA 'SubtitleStudio'
Check 'גם נתיב עם עברית יוצא ASCII' (IsAscii (MkScript "$heb\a.exe" "$heb\b.exe")) ''

# ---------- קלט פגום שהוא בעצם תקין ----------
# קובצי SRT מהעולם האמיתי כותבים את החץ בכל דרך אפשרית. עד עכשיו כל אחד
# מאלה נפתח **ריק**, והמשתמש קיבל הודעה שגויה: "נראה שזה קובץ טקסט רגיל".
Write-Host 'חותמות זמן חריגות'
function Cues1($body) { return ,$fmt.GetMethod('ParseSrt').Invoke($null, (Pack ([string]$body))) }

# בונים מראה, לא בתוך @(...) - שם PowerShell מפרש את השרשור אחרת
$ac = [char]0x060C
$arabicComma = "1`n00:00:01${ac}000 --> 00:00:02${ac}000`nשלום`n"

$variants = @(
    @('חץ קצר',        "1`n00:00:01,000 -> 00:00:02,000`nשלום`n"),
    @('חץ ארוך',       "1`n00:00:01,000 ---> 00:00:02,000`nשלום`n"),
    @('חץ כפול',       "1`n00:00:01,000 -->> 00:00:02,000`nשלום`n"),
    @('רווח בתוך החץ', "1`n00:00:01,000 - > 00:00:02,000`nשלום`n"),
    @('נקודות בזמן',   "1`n00.00.02,000 --> 00.00.04,000`nשלום`n"),
    @('פסיק ערבי',     $arabicComma),
    @('בלי רווחים',    "1`n00:00:01,000-->00:00:02,000`nשלום`n")
)
foreach ($v in $variants) {
    $c = Cues1 $v[1]
    Check ('נקרא: ' + $v[0]) ($c.Count -eq 1 -and $c[0].Text -eq 'שלום') ("count=" + $c.Count)
}

# ומה שכבר עבד חייב להמשיך לעבוד
$ok = @(
    @('רגיל',        "1`n00:00:01,000 --> 00:00:02,000`nשלום`n"),
    @('בלי שעות',    "1`n00:04,480 --> 00:06,000`nשלום`n"),
    @('זבל בסוף',    "1`n00:00:01,000 --> 00:00:02,000  X1:100 X2:200`nשלום`n"),
    @('נקודה באלפיות',"1`n00:00:01.000 --> 00:00:02.000`nשלום`n")
)
foreach ($v in $ok) {
    $c = Cues1 $v[1]
    Check ('עדיין נקרא: ' + $v[0]) ($c.Count -eq 1 -and $c[0].Text -eq 'שלום') ("count=" + $c.Count)
}

# שורת דיאלוג עם חץ היא טקסט, לא זמן - אסור שהנרמול יבלע אותה
$dlg = "1`n00:00:01,000 --> 00:00:05,000`nHe said -> go away`n"
$c = Cues1 $dlg
Check 'חץ בתוך דיאלוג נשאר טקסט' ($c.Count -eq 1 -and $c[0].Text -eq 'He said -> go away') `
    ("count=" + $c.Count + " text=" + $(if ($c.Count) { $c[0].Text } else { '' }))

# ---------- MicroDVD: קצב פריימים ----------
# הקובץ מכריז על הקצב בשורה הראשונה. בלי לקרוא אותה קיבלנו כתובית מזויפת
# שכתוב בה "23.976", ודריפט שמגיע לארבע דקות בסוף סרט.
Write-Host 'MicroDVD'
$subText = "{1}{1}23.976`n{960}{2880}שלום עולם`n{143928}{144000}סוף הסרט`n"
$fpsFound = $fmt.GetMethod('MicroDvdFps').Invoke($null, (Pack ([string]$subText) ([double]0)))
Check 'הקצב נקרא מהקובץ' ([Math]::Abs($fpsFound - 23.976) -lt 0.001) $fpsFound
$sf = [Enum]::Parse($fmtT, 'Sub')
$subCues = $prT.GetField('Cues').GetValue($fmt.GetMethod('ParseText').Invoke($null, (Pack ([string]$subText) $sf)))
Eq 'שורת הקצב אינה כתובית' $subCues.Count 2
Check 'אין כתובית שכתוב בה 23.976' (-not ($subCues[0].Text -match '23')) $subCues[0].Text
# 143928 פריימים ב-23.976 = 6003 שניות, לעומת 5757 לפי 25
$last = $subCues[$subCues.Count - 1].Start
Check 'התזמון לפי הקצב האמיתי' ([Math]::Abs($last - 6003000) -lt 3000) ("start=$last")

# בלי הכרזה - נופלים לקצב של הסרט הפתוח
$noHdr = "{240}{480}שלום`n"
$fps2 = $fmt.GetMethod('MicroDvdFps').Invoke($null, (Pack ([string]$noHdr) ([double]30.0)))
Eq 'נופלים לקצב של הסרט' $fps2 30.0

# ---------- ישויות HTML ב-VTT ----------
Write-Host 'VTT'
$vtt = "WEBVTT`n`n00:00:01.000 --> 00:00:02.000`nTom &amp; Jerry &lt;fast&gt;`n"
$vc = $fmt.GetMethod('ParseVtt').Invoke($null, (Pack ([string]$vtt)))
Check 'ישויות HTML מפוענחות' ($vc.Count -eq 1 -and $vc[0].Text -eq 'Tom & Jerry <fast>') `
    ($(if ($vc.Count) { $vc[0].Text } else { 'none' }))

# ---------- UTF-16 בלי BOM ----------
Write-Host 'קידוד'
$u16 = [System.Text.Encoding]::Unicode.GetBytes("1`r`n00:00:01,000 --> 00:00:02,000`r`nשלום עולם`r`n")
$encOut = ''
$dsArgs = New-Object object[] 2
$dsArgs[0] = $u16
$dsArgs[1] = $null
$decoded = $fmt.GetMethod('DecodeSmart').Invoke($null, $dsArgs)
Check 'UTF-16 בלי BOM מזוהה' ($decoded -match 'שלום עולם') ("enc=" + $dsArgs[1])

# ---------- ערוץ העדכון ----------
# ההכרעה בין "מותקן" ל"נייד" קובעת איזה קובץ יורד ואיך מוחלף. עד עכשיו
# Check לקח את הנכס הראשון שנגמר ב-exe ואת ה-size הראשון בכל ה-JSON,
# וזו הגרלה ברגע שמהדורה נושאת שני קבצים.
Write-Host 'ערוץ העדכון'
$relT = $asm.GetType('SubtitleStudio.Updater+Release')
Check 'Release יודע על שני נכסים' `
    (($null -ne $relT.GetField('SetupUrl')) -and ($null -ne $relT.GetField('SetupSize'))) ''

$instT = $asm.GetType('SubtitleStudio.Install')
Check 'יש זיהוי מותקן/נייד' ($null -ne $instT) ''
if ($instT) {
    # הבדיקה רצה מ-dist, בלי installed.txt ובלי רישום שמצביע לשם
    $isInst = $instT.GetMethod('IsInstalled').Invoke($null, @())
    Check 'עותק מ-dist מזוהה כנייד' (-not $isInst) ("IsInstalled=$isInst")
}

# הביטוי שמזהה את הנכסים - נבדק על JSON אמיתי בצורתו
$sample = @'
{"tag_name":"v9.9.9","assets":[
{"name":"SubtitleStudio.exe","size":38309376,"browser_download_url":"https://github.com/x/y/releases/download/v9.9.9/SubtitleStudio.exe"},
{"name":"SubtitleStudio-Setup.exe","size":38409728,"browser_download_url":"https://github.com/x/y/releases/download/v9.9.9/SubtitleStudio-Setup.exe"}]}
'@
$rx = '"name"\s*:\s*"([^"]+\.exe)"[\s\S]{0,900}?"size"\s*:\s*(\d+)[\s\S]{0,900}?"browser_download_url"\s*:\s*"([^"]+)"'
$plain = ''; $setup = ''; $plainSize = 0; $setupSize = 0
foreach ($m in [regex]::Matches($sample, $rx)) {
    $nm = $m.Groups[1].Value; $u = $m.Groups[3].Value
    if ($u -notmatch '/releases/download/') { continue }
    if ($nm -match 'setup') { $setup = $u; $setupSize = [long]$m.Groups[2].Value }
    elseif ($plain -eq '') { $plain = $u; $plainSize = [long]$m.Groups[2].Value }
}
Check 'הנייד זוהה נכון'  ($plain -match 'SubtitleStudio\.exe$') $plain
Check 'המתקין זוהה נכון' ($setup -match 'Setup\.exe$') $setup
Eq   'גודל הנייד'   $plainSize 38309376
Eq   'גודל המתקין'  $setupSize 38409728

# ---------- סכמת הפעולות של ה-AI ----------
# פעולה בלי פרמטרים חייבת לצאת בלי parameters בכלל. סכמה עם properties
# ריק נדחית בשרת, וכל הבקשה נופלת - כולל הפעולות שכן תקינות.
Write-Host 'סכמת ה-AI'
$aiT = & $T 'Ai'
$toolT = & $T 'AiTool'
$msgT = & $T 'AiMsg'

$listToolT = [System.Collections.Generic.List``1].MakeGenericType($toolT)
$tools = [Activator]::CreateInstance($listToolT)
$noArgs = $toolT.GetConstructor([Type[]]@([string],[string])).Invoke(@([string]'do_nothing', [string]'בלי פרמטרים'))
$withArgs = $toolT.GetConstructor([Type[]]@([string],[string])).Invoke(@([string]'do_thing', [string]'עם פרמטר'))
[void]$toolT.GetMethod('Req').Invoke($withArgs, (Pack ([string]'n') ([string]'integer') ([string]'מספר')))
$listToolT.GetMethod('Add').Invoke($tools, (Pack $noArgs))
$listToolT.GetMethod('Add').Invoke($tools, (Pack $withArgs))

$listMsgT = [System.Collections.Generic.List``1].MakeGenericType($msgT)
$hist = [Activator]::CreateInstance($listMsgT)
$m = [Activator]::CreateInstance($msgT)
$msgT.GetField('Text').SetValue($m, 'בדיקה')
$listMsgT.GetMethod('Add').Invoke($hist, (Pack $m))

# בלי מפתח Send יוצא מוקדם, אז בונים את הגוף דרך אותו קוד בעזרת ההשתקפות
# על BuildBody אם קיים; אחרת בודקים את הסכמה דרך המחלקה עצמה.
$bb = $aiT.GetMethod('BuildBody', [Reflection.BindingFlags]'NonPublic,Public,Static')
if ($bb) {
    $json = $bb.Invoke($null, (Pack ([string]'sys') $hist $tools $false ([string]'gemini-2.5-flash')))
    Check 'פעולה בלי פרמטרים - בלי parameters' ($json -notmatch '"properties"\s*:\s*\{\s*\}') ''
    Check 'פעולה עם פרמטר - יש properties'     ($json -match '"properties"') ''
    Check 'thinkingBudget מכובה ב-2.5'          ($json -match '"thinkingBudget"') ''
    $json2 = $bb.Invoke($null, (Pack ([string]'sys') $hist $tools $false ([string]'gemini-2.0-flash')))
    Check 'ובלי thinkingBudget בדגם ישן'        ($json2 -notmatch '"thinkingBudget"') ''
} else {
    Check 'BuildBody נגיש לבדיקה' $false 'הפרידו את בניית הגוף למתודה כדי שאפשר יהיה לבדוק אותה'
}

# ---------- כיוון טקסט בצריבה ----------
# כתובית ״3 דברים שכדאי לדעת״ נצרבה כ״דברים שכדאי לדעת 3״ - הספרה
# קפצה לקצה השמאלי. אומת מול פיקסלים אמיתיים, ואומת גם שהתיקון מחזיר
# בדיוק את מה ש-GDI+ של ווינדוס נותן לאותו טקסט.
# ‏Subtitle Edit סוחבים את הבאג הזה פתוח (issue #2768).

Write-Host 'כיוון טקסט (bidi)'
$RLM = [char]0x200F
$ST2 = [Reflection.BindingFlags]'NonPublic,Public,Static'
$listCueT2 = [System.Collections.Generic.List`1].MakeGenericType($cueT)
$fx = $fmt.GetMethod('RtlFix', $ST2)
function Fix($t) { return $fx.Invoke($null, (Pack ([string]$t))) }

Check 'ספרה בהתחלה מקבלת עוגן' ((Fix '3 דברים') -eq ($RLM + '3 דברים')) ''
Check 'גרש בהתחלה מקבל עוגן'   ((Fix '"שלום" אמר') -eq ($RLM + '"שלום" אמר')) ''
Check 'לועזית לפני עברית מקבלת עוגן' ((Fix 'Word הוא תוכנה') -eq ($RLM + 'Word הוא תוכנה')) ''

# מה שאסור לגעת בו
Check 'שורה שפותחת בעברית לא משתנה' ((Fix 'שלום עולם') -eq 'שלום עולם') ''
Check 'שורה לועזית לגמרי לא משתנה'  ((Fix '3 red balloons') -eq '3 red balloons') ''
Check 'שורה ריקה לא משתנה'          ((Fix '') -eq '') ''
Check 'טקסט שכבר מעוגן לא מוכפל'    ((Fix ($RLM + '3 דברים')) -eq ($RLM + '3 דברים')) ''

# רב-שורתי: כל שורה נשפטת בנפרד
$multi = Fix ("3 דברים`nשלום`n5 more items")
$want = ($RLM + "3 דברים`nשלום`n5 more items")
Check 'כל שורה נשפטת בנפרד' ($multi -eq $want) ("got=" + ($multi -replace [regex]::Escape($RLM), '<RLM>'))

# רווח מוביל נשמר לפני העוגן
Check 'רווח מוביל נשמר' ((Fix '  3 דברים') -eq ('  ' + $RLM + '3 דברים')) ''

# והחשוב מכל: זה לא דולף לקובץ שהמשתמש שומר
$cuesRtl = [Activator]::CreateInstance($listCueT2)
$listCueT2.GetMethod('Add').Invoke($cuesRtl, (Pack ($cueT.GetConstructor([Type[]]@([long],[long],[string])).Invoke(@([long]0,[long]2000,[string]'3 דברים שכדאי לדעת')))))
$srtOut = $fmt.GetMethod('ToSrt', $ST2).Invoke($null, (Pack $cuesRtl))
Check 'הקובץ הנשמר בלי עוגנים' ($srtOut.IndexOf($RLM) -lt 0) 'ה-RLM דלף ל-SRT'
$assOut = $fmt.GetMethod('ToAss', $ST2).Invoke($null, (Pack $cuesRtl $null ([int]1280) ([int]720)))
Check 'ה-ASS לצריבה כן מעוגן' ($assOut.IndexOf($RLM) -ge 0) 'חסר RLM ב-ASS'

# ---------- תמלול אוטומטי ----------
# החיתוך והאיחוי הם החלק שאפשר לבדוק בלי רשת, והם גם החלק שנשבר בשקט:
# כפילות בגבול בין קטעים לא מייצרת שום שגיאה - רק כתובית כפולה בקובץ.

Write-Host 'תמלול אוטומטי'
$ST2 = [Reflection.BindingFlags]'NonPublic,Public,Static'
$trT = & $T 'Transcribe'
Check 'מחלקת התמלול קיימת' ($null -ne $trT) ''
if ($trT) {
    Eq 'אורך קטע' ($trT.GetField('ChunkSec', $ST2).GetValue($null)) 60
    Eq 'חפיפה'    ($trT.GetField('OverlapSec', $ST2).GetValue($null)) 5

    $dd = $trT.GetMethod('Dedupe', $ST2)
    $ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
    $listCueT = [System.Collections.Generic.List`1].MakeGenericType($cueT)
    # בונים את הרשימה בתוך אותה פונקציה שמשתמשת בה: פונקציה שמחזירה
    # List<T> נפרסת ל-object[] בדרך החוצה, וזו המלכודת שמתועדת ב-CLAUDE.md.
    function DedupCount($rows) {
        $l = [Activator]::CreateInstance($listCueT)
        $add = $listCueT.GetMethod('Add')
        foreach ($r in $rows) {
            $c = $ctor.Invoke(@([long]$r[0], [long]$r[1], [string]$r[2]))
            [void]$add.Invoke($l, (Pack $c))
        }
        $r2 = $dd.Invoke($null, (Pack $l))
        return $r2.Count
    }

    Eq 'כפילות בגבול נזרקת' `
       (DedupCount @(@(1000,3000,'שלום לכולם'), @(1050,3100,'שלום לכולם'), @(5000,7000,'משפט אחר'))) 2
    Eq 'אותו טקסט רחוק בזמן נשמר' `
       (DedupCount @(@(1000,3000,'שלום לכולם'), @(3400,5000,'שלום לכולם'), @(6000,8000,'עוד'))) 3
    Eq 'טקסט שונה לא נזרק' `
       (DedupCount @(@(1000,3000,'שלום לכולם'), @(1050,3100,'טקסט אחר לגמרי'))) 2
    Eq 'אמירה שנחתכה בגבול מזוהה' `
       (DedupCount @(@(1000,3000,'שלום לכולם היום נלמד'), @(1100,2900,'שלום לכולם היום'))) 1
    Eq 'פיסוק לא מונע זיהוי כפילות' `
       (DedupCount @(@(1000,3000,'שלום, לכולם!'), @(1050,3100,'שלום לכולם'))) 1
    Eq 'שתי מילים בלבד לא מאוחדות בטעות' `
       (DedupCount @(@(1000,3000,'כן טוב'), @(1050,3100,'כן רע'))) 2

    # AiMsg נושא אודיו, ו-BuildBody מוציא inline_data - זה מה שמאפשר תמלול
    $msgT2 = & $T 'AiMsg'
    Check 'AiMsg נושא שדה אודיו' ($null -ne $msgT2.GetField('AudioB64')) ''
    $bb2 = (& $T 'Ai').GetMethod('BuildBody', $ST2)
    if ($bb2 -and $msgT2.GetField('AudioB64')) {
        $listMsgT2 = [System.Collections.Generic.List`1].MakeGenericType($msgT2)
        $h2 = [Activator]::CreateInstance($listMsgT2)
        $am = [Activator]::CreateInstance($msgT2)
        $msgT2.GetField('Text').SetValue($am, 'תמלל')
        $msgT2.GetField('AudioB64').SetValue($am, 'QUJD')
        $listMsgT2.GetMethod('Add').Invoke($h2, (Pack $am))
        $j3 = $bb2.Invoke($null, (Pack ([string]'sys') $h2 $null $true ([string]'gemini-2.5-flash')))
        Check 'הבקשה כוללת inline_data' ($j3 -match '"inline_data"') ''
        Check 'ובתוכו הבייטים'          ($j3 -match '"QUJD"') ''
        Check 'וההוראה לפני השמע'       ($j3.IndexOf('תמלל') -lt $j3.IndexOf('QUJD')) ''
    }

    Check 'TrLine קיים' ($null -ne (& $T 'Ai+TrLine')) ''
}

Write-Host ("{0} passed, {1} failed" -f $pass, $fail) -ForegroundColor $(if ($fail) { 'Red' } else { 'Green' })
if ($fail) { exit 1 }
