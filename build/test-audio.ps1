# test-audio.ps1 - **קובץ קול הוא אזרח שווה** (0.8.48). בנימין (25.11): ״התוכנה מתייחסת רק לסרטון... אני רוצה שזה יתאים גם לאודיו, בקובץ MP3
# באותה מידה ולכל פורמט אחר״ - ו״ניסיתי היום להקטין גודל של אודיו, והתוכנה עשתה שגיאה״ (התאמה לגודל על MP3 ניסתה לקודד תמונה שאין).
# הכלים רצים באמת, דרך BuildJob של החלון, על קבצים שנוצרים כאן; והתפריטים וחלון הכלים נקראים כשקובץ קול פתוח.
param([string]$Lang = 'he')
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }; return ,$a }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
$script:pass = 0; $script:fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ('  ok    ' + $name) -ForegroundColor DarkGray }
    else { $script:fail++; Write-Host ('  FAIL  ' + $name + '   ' + $detail) -ForegroundColor Red }
}
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]1.25)
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$ffx = [string](TY 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
$dir = Join-Path $env:TEMP ('ss-audio-' + [guid]::NewGuid().ToString('N').Substring(0, 8)); New-Item -ItemType Directory -Force $dir | Out-Null
function Ffm($a) { $ErrorActionPreference = 'Continue'; $p = Start-Process $ffx -ArgumentList ('-hide_banner -nostdin -v error -y ' + $a) -WorkingDirectory $dir -NoNewWindow -Wait -PassThru; if ($p.ExitCode -ne 0) { throw ('ffmpeg: ' + $a) } }
function Probe($p) { return (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$p)) }
function PropOf($o, $n) { return $o.GetType().GetProperty($n, $IN).GetValue($o, $null) }
function RunJob($job) { [void](TY 'Ff').GetMethod('RunJob', $ST).Invoke($null, (Pack $job)); $sw = [Diagnostics.Stopwatch]::StartNew(); while (-not $job.Done) { Start-Sleep -Milliseconds 60; if ($sw.Elapsed.TotalSeconds -gt 120) { $job.Cancel(); return 'timeout' } }; if ($job.Succeeded) { return '' }; return (@($job.Log.ToString() -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 2) -join ' | ') }
function Fld($o, $n) { return $o.GetType().GetField($n, $IN).GetValue($o) }
function Mb($p) { return (Get-Item $p).Length / 1MB }

# ---- קבצים: ״דיבור״ מדומה (צליל שעולה ויורד עם שתיקות), 20 שניות ----
$src = 'aevalsrc=exprs=''0.4*sin(2*PI*(220+90*sin(2*PI*3*t))*t)*gt(sin(2*PI*0.7*t)\,-0.3)|0.4*sin(2*PI*(330+60*sin(2*PI*2*t))*t)*gt(sin(2*PI*0.7*t)\,-0.3)'':s=44100:d=20'
Ffm "-f lavfi -i `"$src`" -c:a pcm_s16le a.wav"
Ffm '-f lavfi -i color=c=0x336699:s=300x300 -frames:v 1 cover.png'
Ffm '-i a.wav -i cover.png -map 0:a -map 1:v -c:a libmp3lame -b:a 160k -id3v2_version 3 -disposition:v attached_pic a.mp3'
Ffm '-i a.wav -c:a aac -b:a 160k a.m4a'
Ffm '-i a.wav -c:a flac a.flac'
Ffm '-i a.wav -c:a libopus -b:a 96k a.opus'
Ffm '-i a.wav -c:a libvorbis -q:a 5 a.ogg'
Ffm '-i a.wav -c:a wmav2 -b:a 128k a.wma'
Ffm '-i a.wav -ac 1 -ar 8000 -c:a libmp3lame -b:a 16k phone.mp3'
Ffm '-i a.wav -c:a libmp3lame -b:a 64k -ac 1 small.mp3'
Ffm '-i a.wav -i a.wav -map 0:a -map 1:a -c:a aac -b:a 96k -metadata:s:a:0 language=heb -metadata:s:a:1 language=eng two.m4a'
Ffm '-f lavfi -i testsrc2=s=320x240:r=25:d=6 -i a.wav -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac v.mp4'
Ffm '-f lavfi -i testsrc2=s=320x240:r=25:d=3 -an -c:v libx264 -pix_fmt yuv420p mute.mp4'

$tools = @((TY 'MediaTools').GetMethod('All', $ST).Invoke($null, @()))
function ToolNamed($he) { foreach ($t in $tools) { if ($t.Name -eq $he) { return $t } }; throw "no tool $he" }
$unfit = (TY 'ToolsDlg').GetMethod('Unfit', $ST)
function UF($t, $m) { $a = New-Object object[] 2; $a[0] = $t.psobject.BaseObject; $a[1] = $m.psobject.BaseObject; return $unfit.Invoke($null, $a) }

# ---- אילו כלים ----
Write-Host 'אילו כלים יש לקובץ קול'
$mp3 = Probe (Join-Path $dir 'a.mp3'); $vid = Probe (Join-Path $dir 'v.mp4'); $mute = Probe (Join-Path $dir 'mute.mp4')
$fitA = @($tools | Where-Object { -not (UF $_ $mp3) } | ForEach-Object { $_.Name })
$need = 'הכנה לשליחה בוואטסאפ', 'התאמה לגודל קובץ מבוקש', 'דחיסה - הקטנת נפח הקובץ', 'המרת פורמט', 'שינוי מהירות', 'חיבור שני קבצים', 'דהייה בהתחלה ובסוף', 'היפוך לאחור (ריוורס)', 'שינוי עוצמת השמע', 'ניקוי רעש רקע מהקול', 'איזון עוצמה אוטומטי', 'הוספת מוזיקת רקע', 'בחירת ערוץ שמע'
Check 'כל 13 הכלים שעובדים על קול פתוחים לקובץ קול (עד 0.8.47 הקטנה, דחיסה, וואטסאפ, דהייה וחיבור - כבויים או נכשלים)' ($fitA.Count -eq 13 -and @($need | Where-Object { $fitA -notcontains $_ }).Count -eq 0) ($fitA -join ' | ')
Check 'כלי של תמונה שנפתח ישירות על קול (תפריט, עוזר): הסבר, לא ריצה שנכשלת' ((UF (ToolNamed 'שינוי רזולוציה') $mp3) -match 'תמונה') ''
Check 'בסרט - כל 20 הכלים; בסרט בלי קול - כלי הקול אומרים שאין קול' (@($tools | Where-Object { (UF $_ $vid) }).Count -eq 0 -and (UF (ToolNamed 'שינוי עוצמת השמע') $mute) -match 'אין קול') ''
$td = NewOf 'ToolsDlg' @($null, $mp3, [int64]-1, [int64]-1, [int64]0)
function AllBtns($c) { foreach ($x in $c.Controls) { if ($x.GetType().Name -eq 'Btn') { $x }; AllBtns $x } }
$btns = @(AllBtns $td | Where-Object { $_.Sub })
$badT = @($btns | Where-Object { ($_.Text + ' ' + $_.Sub).Replace('וידאו או קול', '') -match 'סרט|וידאו' } | ForEach-Object { $_.Text + ' - ' + $_.Sub })
Check 'חלון הכלים על קובץ קול: 13 כפתורים, כולם פעילים, ואף אחד לא מדבר על ״הסרט״' ($btns.Count -eq 13 -and @($btns | Where-Object { -not $_.Enabled }).Count -eq 0 -and $badT.Count -eq 0) ("n=" + $btns.Count + ' ' + ($badT -join ' | '))
$td.Dispose()

# ---- כל כלי, על כל סוג, בכל אפשרות ----
Write-Host 'כל כלי שנפתח לקובץ קול - רץ ויוצא קובץ קול תקין'
$bad = @(); $runs = 0; $over = @(); $grew = @()
foreach ($f in 'a.mp3', 'a.m4a', 'a.wav', 'a.flac', 'a.opus', 'a.ogg', 'a.wma', 'phone.mp3') {
    $p = Join-Path $dir $f; $mi = Probe $p; $dur = $mi.DurationSec
    foreach ($tool in $tools) {
        if ((UF $tool $mi)) { continue }
        $n = 1
        $d0 = NewOf 'ToolRunDlg' @($null, $tool, $mi, [int64]-1, [int64]-1, [int64]0); $cb = Fld $d0 '_combo'; if ($cb) { $n = $cb.Items.Count }; $d0.Dispose()
        if ($tool.Name -match 'גודל קובץ') { $n = 3 }
        for ($k = 0; $k -lt $n; $k++) {
            $d = NewOf 'ToolRunDlg' @($null, $tool, $mi, [int64]-1, [int64]-1, [int64]0)
            $cb = Fld $d '_combo'; if ($cb) { $cb.SelectedIndex = $k }
            $sl = Fld $d '_slider'
            if ($tool.Name -match 'גודל קובץ') { $sl.Value = [Math]::Max($sl.Min, [Math]::Round((Mb $p) * @(0.3, 0.5, 0.8)[$k], 2)) }
            if ($tool.ParamKind -eq 3) { (Fld $d '_fileField').Text = (Join-Path $dir 'a.ogg') }
            $bl = $d.GetType().GetProperty('Blocked', $IN).GetValue($d, $null)
            if ($bl) { $d.Dispose(); continue }
            $sugg = (Fld $d '_out').Text
            $o = Join-Path $dir ('o' + $runs + [IO.Path]::GetExtension($sugg))
            $job = $d.GetType().GetMethod('BuildJob', $IN).Invoke($d, @([string]$o)); $o = $job.OutputPath
            $val = if ($sl) { $sl.Value } else { 0 }; $d.Dispose()
            $runs++
            $err = RunJob $job
            $mo = if (-not $err -and (Test-Path $o)) { Probe $o } else { $null }
            $want = if ($tool.Name -match 'מהירות') { $dur / $val } elseif ($tool.Name -match 'חיבור') { $dur + 20 } else { $dur }
            if ($err -or $mo -eq $null -or -not $mo.HasAudio -or $mo.HasVideo -or [Math]::Abs($mo.DurationSec - $want) -gt 0.7) { $bad += ($f + ' / ' + $tool.Name + ' #' + $k + ': ' + $(if ($err) { $err } elseif ($mo) { 'dur ' + $mo.DurationSec + ' want ' + $want + ' video=' + $mo.HasVideo } else { 'no file' })) }
            elseif ($tool.Name -match 'גודל קובץ' -and (Mb $o) -gt $val) { $over += ($f + ': ' + $val + ' -> ' + (Mb $o)) }
            elseif ($tool.Name -match 'דחיסה' -and (Mb $o) -ge (Mb $p)) { $grew += ($f + ' #' + $k + ': ' + (Mb $p) + ' -> ' + (Mb $o)) }
            if (Test-Path $o) { Remove-Item $o -Force }
        }
    }
}
Check 'כל כלי על שמונה סוגי קול, בכל אפשרות - יצא קול באורך הנכון' ($runs -ge 130 -and $bad.Count -eq 0) ("runs=$runs  " + ($bad -join ' || '))
Check '״התאמה לגודל״ על קול: אף פעם לא מעל היעד' ($over.Count -eq 0) ($over -join ' | ')
Check '״דחיסה״ על קול: תמיד קטן מהמקור' ($grew.Count -eq 0) ($grew -join ' | ')

# ---- התכנון ----
Write-Host 'התכנון'
$as = TY 'AudioShrink'
function Desc($f) { return $as.GetMethod('Describe', $IN).Invoke($f, @()) }
# הקובץ שנכשל אצל בנימין: MP3 של שעתיים ו-8 דקות, 139 מגה
$big = NewOf 'MediaInfo' @(); $big.Path = 'C:\x\shiur.mp3'; $big.DurationSec = 7676; $big.SizeBytes = [long](139 * 1MB); $big.HasAudio = $true
$s0 = NewOf 'MediaStream' @(); $s0.Type = 'audio'; $s0.Codec = 'mp3'; $s0.Channels = 2; $s0.SampleRate = 44100; $s0.BitRate = 145000; $big.Streams.Add($s0)
$f70 = $as.GetMethod('ForSize', $ST).Invoke($null, (Pack $big ([double]70)))
Check 'שעתיים של MP3 ל-70 מגה: ‏64k, נכנס, ו״טובה לדיבור״' ($f70.Kbps -eq 64 -and $f70.OutMb -lt 70 -and -not $f70.TooSmall -and (Desc $f70) -match 'לדיבור') ("" + $f70.Kbps + ' ' + (Desc $f70))
$f5 = $as.GetMethod('ForSize', $ST).Invoke($null, (Pack $big ([double]5)))
Check 'ל-5 מגה - אומר שקטן מדי (ולא מייצר קובץ שלא נכנס)' ($f5.TooSmall -and (Desc $f5) -match 'קטן מדי') (Desc $f5)
$rg = (TY 'MediaTools').GetMethod('FitRange', $ST).Invoke($null, (Pack $big))
Check 'הפס נפתח על חצי מהקובץ, ומתחיל בגודל הקטן ביותר שעוד נשמע (15)' ($rg[0] -eq 15 -and $rg[1] -eq 135 -and $rg[2] -eq 70) ($rg -join ',')
$args64 = $as.GetMethod('Args', $ST).Invoke($null, (Pack '.mp3' 64 $big)); $args16 = $as.GetMethod('Args', $ST).Invoke($null, (Pack '.mp3' 16 $big)); $args128 = $as.GetMethod('Args', $ST).Invoke($null, (Pack '.mp3' 128 $big))
Check 'קצב נמוך - ערוץ אחד ודגימה נמוכה; קצב גבוה - כמו המקור' ($args64 -match '-ac 1$' -and $args16 -match '-ac 1 -ar 16000' -and $args128 -match '-ac 2$') "$args64 | $args16 | $args128"
$sm = Probe (Join-Path $dir 'small.mp3')
$ph = (ToolNamed 'הכנה לשליחה בוואטסאפ'); $cx = [Activator]::CreateInstance((TY 'ToolCtx')); $cx.Mi = $sm
Check '״וואטסאפ״ על MP3 קטן: אומר שאפשר לשלוח כמו שהוא' ($ph.Block.Invoke($cx) -match 'כמו שהוא') ''
$dW = NewOf 'ToolRunDlg' @($null, (ToolNamed 'התאמה לגודל קובץ מבוקש'), $mp3, [int64]-1, [int64]-1, [int64]0)
Check 'הקטנה: שם עם ‎.wav שהוקלד ביד - נשמר דחוס' ([IO.Path]::GetExtension($dW.GetType().GetMethod('FinalPath', $IN).Invoke($dW, @('C:\x\y.wav'))) -eq '.mp3') ''
$dW.Dispose()

# ---- המרת פורמט ----
Write-Host 'המרת פורמט'
$conv = ToolNamed 'המרת פורמט'
$dc = NewOf 'ToolRunDlg' @($null, $conv, $mp3, [int64]-1, [int64]-1, [int64]0); $cb = Fld $dc '_combo'
$names = @(0..($cb.Items.Count - 1) | ForEach-Object { '' + $cb.Items[$_] }) -join ' | '
Check 'קובץ קול: MP3, ‏M4A, ‏WAV, ‏FLAC ו-OGG - לא MP4 ו-MKV; ונפתח על פורמט אחר מהמקור' ($cb.Items.Count -eq 5 -and $names -match 'MP3' -and $names -match 'FLAC' -and $names -notmatch 'MP4|MKV' -and $cb.SelectedIndex -eq 1) ($names + ' sel=' + $cb.SelectedIndex)
$dc.Dispose()
$dv = NewOf 'ToolRunDlg' @($null, $conv, $vid, [int64]-1, [int64]-1, [int64]0); $cbv = Fld $dv '_combo'
Check 'בסרט - האפשרויות של סרט, כמו קודם' ($cbv.Items.Count -eq 3 -and ('' + $cbv.Items[0]) -match 'MP4') ''
$dv.Dispose()
$mAac = Probe (Join-Path $dir 'a.m4a'); $cx2 = [Activator]::CreateInstance((TY 'ToolCtx')); $cx2.Mi = $mAac; $cx2.In = $mAac.Path; $cx2.Out = (Join-Path $dir 'y.m4a'); $cx2.Opt = 1
Check 'AAC ל-M4A - מועתק, בלי קידוד נוסף' ($conv.Build.Invoke($cx2) -match '-c:a copy') ''

# ---- קובץ נוסף ----
Write-Host 'קובץ נוסף שלא מתאים'
function ExtraMsg($toolName, $mi, $extra) { $c = [Activator]::CreateInstance((TY 'ToolCtx')); $c.Mi = $mi; $c.Extra = $extra; return (ToolNamed $toolName).CheckExtra.Invoke($c) }
$e1 = ExtraMsg 'חיבור שני קבצים' $vid (Join-Path $dir 'a.mp3'); $e2 = ExtraMsg 'חיבור שני קבצים' $mp3 (Join-Path $dir 'mute.mp4'); $e3 = ExtraMsg 'חיבור שני קבצים' $mp3 (Join-Path $dir 'v.mp4'); $e4 = ExtraMsg 'הוספת מוזיקת רקע' $mp3 (Join-Path $dir 'cover.png'); $e5 = ExtraMsg 'חיבור שני קבצים' $mp3 (Join-Path $dir 'none.mp3')
Check 'לסרט אי אפשר לחבר קובץ קול, לקול - סרט בלי קול, ואומר למה; קול מתוך סרט - אפשר' ($e1 -match 'אין תמונה' -and $e2 -match 'אין קול' -and $e3 -eq $null -and $e4 -match 'אין קול' -and $e5 -match 'לא נמצא') "$e1 | $e2 | $e3 | $e4 | $e5"
$two = Probe (Join-Path $dir 'two.m4a'); $trk = ToolNamed 'בחירת ערוץ שמע'
$d2 = NewOf 'ToolRunDlg' @($null, $trk, $two, [int64]-1, [int64]-1, [int64]0); (Fld $d2 '_combo').SelectedIndex = 1
$bl2 = $d2.GetType().GetProperty('Blocked', $IN).GetValue($d2, $null); $j2 = $d2.GetType().GetMethod('BuildJob', $IN).Invoke($d2, @([string](Join-Path $dir 'trk.m4a'))); $d2.Dispose()
$er2 = RunJob $j2; $mt2 = if (-not $er2) { Probe $j2.OutputPath } else { $null }
$cx3 = [Activator]::CreateInstance((TY 'ToolCtx')); $cx3.Mi = $mp3
Check 'בחירת ערוץ שמע בקובץ קול עם שני ערוצים - הערוץ שנבחר; ובקובץ עם אחד - אומר שאין מה לבחור' (-not $bl2 -and $mt2 -and @($mt2.Streams | Where-Object { $_.Type -eq 'audio' }).Count -eq 1 -and $mt2.FirstAudio().Language -eq 'eng' -and $trk.Block.Invoke($cx3) -match 'ערוץ שמע אחד') ("bl=$bl2 err=$er2")

# ---- סרט מקובץ קול ----
Write-Host 'יצירת סרט מקובץ קול'
$doc = NewOf 'Doc' @(); $cues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
[void]$cues.Add((NewOf 'Cue' @([int64]4000, [int64]7000, [string]'שלום, זו כתובית'))); [void]$cues.Add((NewOf 'Cue' @([int64]12000, [int64]15000, [string]'והשנייה')))
$style = NewOf 'SubStyle' @()
function Shot($p, [double]$t, $png) { Ffm ('-ss ' + $t.ToString([Globalization.CultureInfo]::InvariantCulture) + ' -i "' + $p + '" -frames:v 1 -vf scale=480:270 "' + $png + '"'); $b = [Drawing.Bitmap]::FromFile($png); $lit = 0; for ($y = 190; $y -lt 270; $y += 2) { for ($x = 0; $x -lt 480; $x += 2) { if ($b.GetPixel($x, $y).GetBrightness() -gt 0.75) { $lit++ } } }; $mid = $b.GetPixel(240, 100); $b.Dispose(); return @{ Lit = $lit; Mid = $mid } }
$ed = NewOf 'ExportVideoDlg' @($null, $doc, $mp3, $style, [int64]-1, [int64]-1)
$bg = Fld $ed '_bg'
Check 'החלון: ״יצירת וידאו מקובץ הקול״, בוחרים רקע (עטיפה - כשיש), ובלי צריבה/ערוץ נפרד' ((PropOf $ed 'TitleText') -eq 'יצירת וידאו מקובץ הקול' -and $bg.Items.Count -eq 3 -and $bg.SelectedIndex -eq 1 -and (PropOf $ed 'SubtitleText') -notmatch 'fps') ((PropOf $ed 'TitleText') + ' / ' + (PropOf $ed 'SubtitleText') + ' / ' + $bg.Items.Count)
$res = @()
for ($i = 0; $i -lt 3; $i++) {
    $bg.SelectedIndex = $i
    if ($i -eq 2) { (Fld $ed '_bgFile').Text = (Join-Path $dir 'cover.png') }
    $job = $ed.GetType().GetMethod('BuildJob', $IN).Invoke($ed, @([string](Join-Path $dir "m$i.mp4")))
    $err = RunJob $job
    if ($err) { $res += "#$i $err"; continue }
    $mo = Probe $job.OutputPath
    $on = Shot $job.OutputPath 5.5 (Join-Path $dir "on$i.png"); $off = Shot $job.OutputPath 9 (Join-Path $dir "off$i.png"); $on2 = Shot $job.OutputPath 13.5 (Join-Path $dir "on2$i.png")
    if (-not ($mo.HasVideo -and $mo.HasAudio -and $mo.Width -eq 1920 -and $mo.Height -eq 1080 -and [Math]::Abs($mo.DurationSec - 20) -lt 0.6 -and $on.Lit -gt 30 -and $on2.Lit -gt 15 -and $off.Lit -eq 0)) { $res += "#$i dur=$($mo.DurationSec) $($mo.Width)x$($mo.Height) lit=$($on.Lit)/$($on2.Lit)/$($off.Lit)" }
    # הרקע: תמונה כחולה באמצע בעטיפה ובתמונה; כהה כשבחרו כהה
    $blue = $on.Mid.B -gt 110
    if ($blue -ne ($i -gt 0)) { $res += "#$i mid=$($on.Mid)" }
}
Check 'רקע כהה, תמונת העטיפה ותמונה מהמחשב: סרט 1920×1080 עם הקול, והכתוביות בזמן שלהן בלבד' ($res.Count -eq 0) ($res -join ' | ')
Check 'גם כשהוקלדה סיומת של קול - נשמר סרט' ([IO.Path]::GetExtension($ed.GetType().GetMethod('FinalPath', $IN).Invoke($ed, @('C:\x\y.mp3'))) -eq '.mp4') ''
$ed.Dispose()
$er = NewOf 'ExportVideoDlg' @($null, $doc, (Probe (Join-Path $dir 'a.ogg')), $style, [int64]11000, [int64]16000)
(Fld $er '_rangeOnly').Checked = $true; $bgr = Fld $er '_bg'; $nb = $bgr.Items.Count
$job = $er.GetType().GetMethod('BuildJob', $IN).Invoke($er, @([string](Join-Path $dir 'mr.mp4'))); $er.Dispose()
$err = RunJob $job; $mr = if (-not $err) { Probe $job.OutputPath } else { $null }
$on = if ($mr) { Shot $job.OutputPath 2 (Join-Path $dir 'onr.png') } else { @{ Lit = 0 } }
Check 'רק הקטע המסומן, מקובץ בלי עטיפה (שתי אפשרויות רקע): 5 שניות, והכתובית בזמן שלה' ($nb -eq 2 -and $mr -and [Math]::Abs($mr.DurationSec - 5) -lt 0.4 -and $on.Lit -gt 15) ("bg=$nb err=$err lit=" + $on.Lit)
$fl = NewOf 'ExportVideoDlg' @($null, $doc, (Probe (Join-Path $dir 'a.flac')), $style, [int64]-1, [int64]-1)
$job = $fl.GetType().GetMethod('BuildJob', $IN).Invoke($fl, @([string](Join-Path $dir 'mf.mp4'))); $fl.Dispose()
$err = RunJob $job; $mfl = if (-not $err) { Probe $job.OutputPath } else { $null }
Check 'קול שמקודד (FLAC): הסרט נגמר עם הקול (עם shortest לבד יצא ארוך ב-1.3 שניות)' ($mfl -and [Math]::Abs($mfl.DurationSec - 20) -lt 0.3) ("dur=" + $(if ($mfl) { $mfl.DurationSec } else { $err }))
$e8 = NewOf 'ExportVideoDlg' @($null, $doc, (Probe (Join-Path $dir 'phone.mp3')), $style, [int64]-1, [int64]-1)
$job = $e8.GetType().GetMethod('BuildJob', $IN).Invoke($e8, @([string](Join-Path $dir 'm8.mp4'))); $e8.Dispose()
$err = RunJob $job
Check 'MP3 של טלפון (8kHz): לא מועתק ל-MP4 (יצא ריק), מקודד' (-not $err -and (Probe $job.OutputPath).HasAudio) $err

# ---- התפריטים כשקובץ קול פתוח ----
Write-Host 'התפריטים'
$mf = TY 'MainForm'
$m = [Activator]::CreateInstance($mf); $m.StartPosition = 'Manual'; $m.Location = New-Object Drawing.Point -4000, -4000; $m.Size = New-Object Drawing.Size 1400, 900; $m.ShowInTaskbar = $false
$m.Show(); [Windows.Forms.Application]::DoEvents()
[void]$mf.GetMethod('OpenMedia', $IN).Invoke($m, @([string](Join-Path $dir 'v.mp4')))
[void]$mf.GetMethod('OpenMedia', $IN).Invoke($m, @([string](Join-Path $dir 'a.mp3')))
$vp = $mf.GetField('_video', $IN).GetValue($m)
Check 'קובץ קול אחרי סרט בצורה אחרת: התצוגה חוזרת ל-16:9 (כמו הסרט שייצא)' ($vp.AspectW -eq 16 -and $vp.AspectH -eq 9) ("" + $vp.AspectW + ':' + $vp.AspectH)
$mc = (TY 'Doc').GetField('Cues', $IN).GetValue($mf.GetField('_doc', $IN).GetValue($m)); [void]$mc.Add((NewOf 'Cue' @([int64]1000, [int64]3000, [string]'שלום')))
$say = @()
foreach ($it in @($mf.GetMethod('VideoMenuItems', $IN).Invoke($m, @())) + @($mf.GetMethod('SubtitleMenuItems', $IN).Invoke($m, @()))) {
    if (-not $it.Enabled) { continue }
    $t = ('' + $it.Text) + ' ' + ('' + $it.Sub) + ' ' + ('' + $it.Desc)
    if ($t -match 'הסרט|בסרט|וידאו') { $say += $t }
}
Check 'שני התפריטים כשפתוח קובץ קול: שום פריט פעיל לא מדבר על ״הסרט״' ($say.Count -eq 0) ($say -join ' | ')
$sum = (Probe (Join-Path $dir 'a.mp3')).Summary()
Check 'התיאור של קובץ קול - בלי ״25 fps״' ($sum -notmatch 'fps') $sum
[void]$mf.GetMethod('CloseEverything', $IN).Invoke($m, @()); $m.Dispose()

# ---- סרטון של תמונות JPEG הוא וידאו, עטיפה לא (0.8.48; עד אז סרטון ממצלמה ישנה נפתח כקובץ קול) ----
Write-Host 'סרטון MJPEG'
Ffm '-f lavfi -i testsrc2=s=320x240:r=15:d=3 -i a.wav -shortest -c:v mjpeg -q:v 5 -c:a pcm_s16le cam.avi'
Ffm '-f lavfi -i testsrc2=s=320x240:r=15:d=3 -i a.wav -shortest -c:v mjpeg -q:v 5 -c:a aac cam.mov'
Ffm '-f lavfi -i testsrc2=s=320x240:r=15:d=3 -i a.wav -i cover.png -map 0:v -map 1:a -map 2:v -t 3 -c:v:0 libx264 -c:v:1 png -pix_fmt:v:0 yuv420p -c:a aac -disposition:v:1 attached_pic withcover.mp4'
Ffm '-i a.wma -i cover.png -map 0:a -map 1:v -c copy cover.wma'
Ffm '-i a.flac -i cover.png -map 0:a -map 1:v -c copy -disposition:v attached_pic cover.flac'
$ma = Probe (Join-Path $dir 'cam.avi'); $mm = Probe (Join-Path $dir 'cam.mov'); $mc4 = Probe (Join-Path $dir 'withcover.mp4')
Check 'סרטון MJPEG (AVI ו-MOV): וידאו, עם המידות והקצב שלו' ($ma.HasVideo -and $ma.HasAudio -and $ma.Width -eq 320 -and $ma.Fps -eq 15 -and $mm.HasVideo -and $mm.Height -eq 240) ("" + $ma.HasVideo + ' ' + $ma.Width + ' ' + $ma.Fps + ' / ' + $mm.HasVideo)
Check 'תמונת עטיפה - לא וידאו (MP3, ‏FLAC, ו-WMA שלא מסמן אותה); ובווידאו עם עטיפה - הווידאו עצמו' (-not $mp3.HasVideo -and -not (Probe (Join-Path $dir 'cover.flac')).HasVideo -and -not (Probe (Join-Path $dir 'cover.wma')).HasVideo -and $mc4.HasVideo -and $mc4.FirstVideo().Codec -eq 'h264') ''
$dMj = NewOf 'ToolRunDlg' @($null, (ToolNamed 'הכנה לשליחה בוואטסאפ'), $ma, [int64]-1, [int64]-1, [int64]0)
$jMj = $dMj.GetType().GetMethod('BuildJob', $IN).Invoke($dMj, @([string](Join-Path $dir 'cam-out.mp4'))); $dMj.Dispose()
$eMj = RunJob $jMj; $oMj = if (-not $eMj) { Probe $jMj.OutputPath } else { $null }
Check 'וכלי וידאו רץ עליו: וואטסאפ מוציא MP4 עם תמונה וקול' ($oMj -and $oMj.HasVideo -and $oMj.HasAudio -and $oMj.FirstVideo().Codec -eq 'h264') $eMj

[GC]::Collect()
try { Remove-Item $dir -Recurse -Force -ErrorAction Stop } catch { }
Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $script:pass, $script:fail)
if ($script:fail -gt 0) { exit 1 }
