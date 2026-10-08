# real-sweep.ps1 - כל מסלולי המדיה של התוכנה על קבצים אמיתיים מהמחשב, והבדיקה
# של **מה שיצא**, לא רק ״לא קרס״.
#
# הבדיקות האחרות רצות על test.mp4 אחד שנוצר במעבדה. כאן: MKV עם opus, ‏MOV עם
# PCM של 24 ביט, ‏AVI ישן, ‏WMA, ‏WAV ב-ADPCM, ‏MP3 עם תמונת עטיפה. העבודות נבנות
# **בחלונות עצמם** (ExportVideoDlg/ToolRunDlg.BuildJob, ובחיתוך CutPlan כמו שהחלון בונה), כך שנבדקת הפקודה
# שהתוכנה באמת מריצה - ושום חלון לא נפתח מול המשתמש.
#
#   real-sweep.ps1 -List files.txt [-Tools] [-Out dir] [-Ffmpeg path]
# ‏-Tools מריץ גם את כל כלי המדיה על כל קובץ (איטי). הפלט: שורה לכל בדיקה,
# ‏OK או BAD עם הסבר, וקבצים ב-D:\Claude\_ss-sweep (ליד המאגר, לא ב-C: סבב אחד הגיע ל-9GB). מקור לא נכתב לעולם.
param([Parameter(Mandatory = $true)][string]$List, [switch]$Tools, [string]$Out = '', [string]$Ffmpeg = '')
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }; return ,$a }
function Pk { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $v = $args[$i]; if ($null -ne $v) { $v = $v.psobject.BaseObject }; $a[$i] = $v }; return ,$a }
function CutSecs($spec) {
    $T = TY 'CutSection'; $l = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($T)); $id = 1
    foreach ($x in $spec) { $c = [Activator]::CreateInstance($T); $c.Id = $id++; $c.A = [long]$x[0]; $c.B = [long]$x[1]; $c.Keep = [bool]$x[2]; [void]$l.Add($c) }
    return ,$l
}
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]1.25)
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
# ‏-Ffmpeg: מנוע אחר מזה שבתוכנה - כדי להשוות מנוע ישן לחדש על אותם קבצים, באותה בנייה (8.10: ‏9.0.1 מול 9.0.2)
if ($Ffmpeg) { (TY 'Ff').GetField('_exe', $ST).SetValue($null, $Ffmpeg); Write-Host ('engine: ' + $Ffmpeg) }
$ffexe = (TY 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
if (-not $Out) { $Out = Join-Path (Split-Path $root -Parent) '_ss-sweep' }
New-Item -ItemType Directory -Force $Out | Out-Null

$script:ok = 0; $script:bad = 0
$results = New-Object Collections.ArrayList
function Rep($file, $what, $good, $detail) {
    if ($good) { $script:ok++ } else { $script:bad++ }
    $tag = if ($good) { 'OK ' } else { 'BAD' }
    Write-Host ('  {0} {1,-28} {2}' -f $tag, $what, $detail) -ForegroundColor $(if ($good) { 'Gray' } else { 'Red' })
    [void]$results.Add([pscustomobject]@{ File = $file; What = $what; Ok = [bool]$good; Detail = $detail })
}

function Probe($p) { return (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$p)) }
function RunJob($job, [int]$timeoutSec = 900) {
    # ‏[void]: קריאה שמחזירה null פולטת null לצינור, והפונקציה החזירה מערך - שנקרא כשגיאה
    [void](TY 'Ff').GetMethod('RunJob', $ST).Invoke($null, (Pack $job))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $job.Done) { Start-Sleep -Milliseconds 200; if ($sw.Elapsed.TotalSeconds -gt $timeoutSec) { $job.Cancel(); Start-Sleep 1; return 'timeout' } }
    if ($job.Succeeded) { return '' }
    $log = $job.Log.ToString(); $lines = @($log -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 3)
    return ($lines -join ' | ')
}
function Streams($mi, $type) { return @($mi.Streams | Where-Object { $_.Type -eq $type }) }
function Near([double]$a, [double]$b, [double]$tol) { return [Math]::Abs($a - $b) -le $tol }

# פריים מקובץ, מוקטן ל-320 ברוחב, לקובץ PNG
function Frame($path, [double]$sec, $png) {
    $so = ''; $se = ''
    $args2 = '-hide_banner -y -ss ' + $sec.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture) + ' -i "' + $path + '" -frames:v 1 -vf scale=320:-2 "' + $png + '"'
    $argv = New-Object object[] 5; $argv[0] = $ffexe; $argv[1] = $args2; [void](TY 'Ff').GetMethod('RunSync', $ST).Invoke($null, $argv)
    return (Test-Path $png)
}
# ממוצע ההפרש בין שני פריימים בשליש התחתון ובשליש העליון
function BottomDiff($pngA, $pngB) {
    $a = [Drawing.Bitmap]::FromFile($pngA); $b = [Drawing.Bitmap]::FromFile($pngB)
    try {
        $w = [Math]::Min($a.Width, $b.Width); $h = [Math]::Min($a.Height, $b.Height)
        $top = 0.0; $bot = 0.0; $nt = 0; $nb = 0
        for ($y = 0; $y -lt $h; $y += 2) { for ($x = 0; $x -lt $w; $x += 2) {
            $ca = $a.GetPixel($x, $y); $cb = $b.GetPixel($x, $y)
            $d = [Math]::Abs($ca.R - $cb.R) + [Math]::Abs($ca.G - $cb.G) + [Math]::Abs($ca.B - $cb.B)
            if ($y -gt $h * 0.62) { $bot += $d; $nb++ } elseif ($y -lt $h * 0.38) { $top += $d; $nt++ }
        } }
        return @(($top / [Math]::Max(1, $nt)), ($bot / [Math]::Max(1, $nb)))
    } finally { $a.Dispose(); $b.Dispose() }
}

$files = @([IO.File]::ReadAllLines($List, [Text.Encoding]::UTF8) | Where-Object { $_.Trim() -and -not $_.StartsWith('#') })
$extraAudio = $null; $extraVideo = $null
foreach ($f in $files) { $m = Probe $f; if (-not $extraAudio -and $m.HasAudio -and -not $m.HasVideo) { $extraAudio = $f }; if (-not $extraVideo -and $m.HasVideo -and $m.DurationSec -lt 120) { $extraVideo = $f } }

$n = 0
foreach ($f in $files) {
    $n++
    $name = [IO.Path]::GetFileName($f)
    Write-Host ''
    Write-Host ("[{0}/{1}] {2}" -f $n, $files.Count, $f) -ForegroundColor Cyan
    $tag = 'f' + $n
    try { $mi = Probe $f } catch { Rep $name 'probe' $false $_.Exception.InnerException.Message; continue }
    $dur = $mi.DurationSec
    $vid = $mi.FirstVideo() -ne $null
    Rep $name 'probe' ($dur -gt 0) ('{0:0.0}s video={1} audio={2} {3}x{4}' -f $dur, $vid, $mi.HasAudio, $mi.Width, $mi.Height)
    if ($dur -le 0) { continue }

    # ---- פס הקול ----
    if ($mi.HasAudio) {
        $wf = NewOf 'Waveform' @()
        $wf.Build($f, [long]$mi.DurationMs)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while (-not $wf.Ready -and -not $wf.Failed -and $sw.Elapsed.TotalSeconds -lt 300) { Start-Sleep -Milliseconds 200 }
        $filled = 0; $last = 0
        for ($i = 0; $i -lt $wf.Peak.Length; $i++) { if ($wf.Peak[$i] -gt 6) { $filled++; $last = $i } }
        $lastSec = $last * 10 / 1000.0
        $cover = $filled / [Math]::Max(1, $wf.Peak.Length)
        Rep $name 'waveform' ($wf.Ready -and $cover -gt 0.05 -and $lastSec -gt $dur * 0.8) ('{0:0.0}s  ready={1} failed={2} sound in {3:P0} of buckets, last sound at {4:0.0}s of {5:0.0}s' -f $sw.Elapsed.TotalSeconds, $wf.Ready, $wf.Failed, $cover, $lastSec, $dur)
    }

    # ---- כתוביות לדוגמה: שלוש, ב-20%, 50% ו-80% ----
    $doc = NewOf 'Doc' @()
    $cues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
    $texts = @('שלום, זו כתובית ראשונה', 'Second line in English', 'והשלישית: ״מירכאות״ ו-100%')
    $times = @(0.2, 0.5, 0.8) | ForEach-Object { [long]($dur * 1000 * $_) }
    for ($i = 0; $i -lt 3; $i++) { [void]$cues.Add((NewOf 'Cue' @([long]$times[$i], [long]($times[$i] + [Math]::Min(2500, $dur * 100)), [string]$texts[$i]))) }
    $style = NewOf 'SubStyle' @()

    # מעל 1080p הצריבה איטית פי 5 מהזמן האמיתי (נמדד: 804 שניות לקליפ 4K של 142) - לא בכל סבב
    $big = $mi.Width * $mi.Height -gt 2200000
    if ($vid -and $dur -le 400 -and -not $big) {
        # ---- צריבה ----
        $dlg = NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [long]-1, [long]-1)
        $o = Join-Path $Out ($tag + '-burn.mp4')
        $job = $dlg.GetType().GetMethod('BuildJob', $IN).Invoke($dlg, @([string]$o)); $dlg.Dispose(); $o = $job.OutputPath
        $sw = [Diagnostics.Stopwatch]::StartNew(); $err = RunJob $job
        if ($err) { Rep $name 'burn' $false $err }
        else {
            $mo = Probe $o
            $okDur = Near $mo.DurationSec $dur ([Math]::Max(0.6, $dur * 0.01))
            $okA = ($mo.HasAudio -eq $mi.HasAudio)
            # הכתובית באמת בתמונה: באמצע הכתובית השנייה השליש התחתון שונה מהמקור, והעליון לא
            $t = ($times[1] + 800) / 1000.0
            $pa = Join-Path $Out ($tag + '-src.png'); $pb = Join-Path $Out ($tag + '-out.png')
            $fa = Frame $f $t $pa; $fb = Frame $o $t $pb
            $d = if ($fa -and $fb) { BottomDiff $pa $pb } else { @(-1, -1) }
            # נבדק בעין (24.9): בקובץ 4K מוקטן ל-320 הכתובית תופסת מעט, וההפרש 4-8. הסף נמוך בהתאם.
            $okSub = $d[1] -gt 3 -and $d[1] -gt $d[0] * 2.5
            Rep $name 'burn' ($okDur -and $okA -and $okSub) ('{0:0}s  dur {1:0.00}/{2:0.00}  audio {3}  diff top {4:0.0} bottom {5:0.0}' -f $sw.Elapsed.TotalSeconds, $mo.DurationSec, $dur, $mo.HasAudio, $d[0], $d[1])
        }
    }

    if ($vid) {
        # ---- ערוץ כתוביות נפרד, ל-MP4 ול-MKV ----
        foreach ($ext in '.mp4', '.mkv') {
            $dlg = NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [long]-1, [long]-1)
            [void]$dlg.GetType().GetMethod('SetMode', $IN).Invoke($dlg, @($false))
            $o = Join-Path $Out ($tag + '-soft' + $ext)
            $job = $dlg.GetType().GetMethod('BuildJob', $IN).Invoke($dlg, @([string]$o)); $dlg.Dispose(); $o = $job.OutputPath
            $err = RunJob $job
            if ($err) { Rep $name ('soft ' + $ext) $false $err; continue }
            $mo = Probe $o
            $subs = Streams $mo 'subtitle'
            # מחלצים בחזרה ובודקים שהטקסט שרד, כולל העברית והמירכאות
            $srt = Join-Path $Out ($tag + '-back' + $ext + '.srt')
            $so = ''; $se = ''
            $argv = New-Object object[] 5; $argv[0] = $ffexe; $argv[1] = ('-hide_banner -y -i "' + $o + '" -map 0:s:0 "' + $srt + '"'); [void](TY 'Ff').GetMethod('RunSync', $ST).Invoke($null, $argv)
            $back = if (Test-Path $srt) { [IO.File]::ReadAllText($srt, [Text.Encoding]::UTF8) } else { '' }
            $allText = ($texts | Where-Object { $back.Contains($_) }).Count
            Rep $name ('soft ' + $ext) ($subs.Count -ge 1 -and $allText -eq 3 -and (Near $mo.DurationSec $dur 1.0)) ('subtitle streams {0} ({1})  texts back {2}/3  dur {3:0.00}/{4:0.00}' -f $subs.Count, (($subs | ForEach-Object { $_.Codec + ':' + $_.Language }) -join ','), $allText, $mo.DurationSec, $dur)
        }

    }

    # ---- חיתוך: חלון הקטעים. העבודה נבנית כמו ש-CutDlg.OnOk בונה אותה (CutPlan.Kept/FinalPath/BuildJob) ----
    # לשמור קטע (מהיר ומדויק), להסיר קטע, ושני קטעים לשמירה עם הסרה בתוך הראשון. קובץ קול - תמיד העתקה, באותו פורמט.
    if ($dur -le 400 -and -not $big) {
        $D = [long]$mi.DurationMs
        $a = [long]($D * 0.3); $b = [long]($D * 0.3 + [Math]::Min(8000, $D / 4))
        $c0 = [long]($D * 0.6); $c1 = [long]($D * 0.6 + [Math]::Min(6000, $D / 5))
        $r0 = [long]($a + ($b - $a) / 4); $r1 = [long]($a + ($b - $a) / 2)
        $plans = @(
            @{ n = 'fast';  fast = $true;  spec = @(,@($a, $b, $true));                                 want = ($b - $a) },
            @{ n = 'exact'; fast = $false; spec = @(,@($a, $b, $true));                                 want = ($b - $a) },
            @{ n = 'cut';   fast = $false; spec = @(,@($a, $b, $false));                                want = ($D - ($b - $a)) },
            @{ n = 'multi'; fast = $false; spec = @(@($a, $b, $true), @($r0, $r1, $false), @($c0, $c1, $true)); want = (($b - $a) - ($r1 - $r0) + ($c1 - $c0)) })
        foreach ($pl in $plans) {
            if ($pl.n -eq 'exact' -and -not $vid) { continue }        # בקובץ קול אין מצב מדויק - תמיד העתקה
            $secs = CutSecs $pl.spec
            $kept = (TY 'CutPlan').GetMethod('Kept', $ST).Invoke($null, (Pk $secs $D))
            $items = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($kept.GetType()))
            [void]$items.Add($kept)
            $o = (TY 'CutPlan').GetMethod('FinalPath', $ST).Invoke($null, (Pk $mi ([string](Join-Path $Out ($tag + '-trim-' + $pl.n + [IO.Path]::GetExtension($f)))) ([bool]$pl.fast)))
            $outs = New-Object 'Collections.Generic.List[string]'; $outs.Add($o)
            $job = (TY 'CutPlan').GetMethod('BuildJob', $ST).Invoke($null, (Pk $mi $items $outs ([bool]$pl.fast)))
            $err = RunJob $job
            if ($err) { Rep $name ('trim ' + $pl.n) $false $err; continue }
            $mo = Probe $o
            $want = $pl.want / 1000.0
            # חיתוך מהיר מתחיל בפריים המפתח שלפני: עד כמה שניות יותר - זה מה שהחלון מזהיר עליו.
            # העתקה של קול: כל חיבור מוסיף פריים (MP3 - ‏26ms), ובקובץ ארוך התחילה מעוגלת לפריים
            $tol = if ($pl.fast -and $vid) { 10.0 } else { 0.6 }
            $okD = ($mo.DurationSec -ge $want - 0.6) -and ($mo.DurationSec -le $want + $tol)
            $sameFmt = $vid -or ([IO.Path]::GetExtension($o) -eq [IO.Path]::GetExtension($f))
            Rep $name ('trim ' + $pl.n) ($okD -and $sameFmt -and (($mo.FirstVideo() -ne $null) -eq $vid) -and $mo.HasAudio -eq $mi.HasAudio) ('dur {0:0.00} want {1:0.00}  video {2} audio {3}  {4}' -f $mo.DurationSec, $want, ($mo.FirstVideo() -ne $null), $mo.HasAudio, [IO.Path]::GetFileName($o))
        }
    }

    # ---- כלי המדיה ----
    if ($Tools) {
        $all = (TY 'MediaTools').GetMethod('All', $ST).Invoke($null, @())
        $a = [long]($dur * 1000 * 0.3); $b = [long]($dur * 1000 * 0.3 + [Math]::Min(6000, $dur * 200))
        $ti = 0
        foreach ($tool in $all) {
            $ti++
            $tn = $tool.Name
            if ($tool.NeedsVideo -and -not $vid) { continue }
            $dlg = NewOf 'ToolRunDlg' @($null, $tool, $mi, $a, $b, [long]($dur * 500))
            if ($tool.ParamKind -eq 3) {
                $ff = $dlg.GetType().GetField('_fileField', $IN).GetValue($dlg)
                $other = if ($tn -match 'חיבור') { $extraVideo } else { $extraAudio }
                if (-not $other) { $dlg.Dispose(); continue }
                $ff.Text = $other
            }
            # ״התאמה לגודל״: יעד של חצי מהמקור (בצעדים של 5 מגה, לא פחות מ-5) - כדי שהכלי באמת יעבוד, ונבדוק שנכנס
            $slider0 = $dlg.GetType().GetField('_slider', $IN).GetValue($dlg)
            $srcMb = (Get-Item $f).Length / 1MB
            if ($tn -match 'גודל קובץ' -and $slider0) { $slider0.Value = [Math]::Max(5, [Math]::Floor($srcMb * 0.5 / 5) * 5) }
            # ״הפעלה״ כבויה (MediaTool.Block, 8.10): מותר רק כשבאמת אין מה לעשות - קובץ שכבר קטן מהיעד
            $blocked = $dlg.GetType().GetProperty('Blocked', $IN).GetValue($dlg, $null)
            if ($blocked) {
                $okBlock = ($tn -match 'גודל קובץ' -and $srcMb -le $slider0.Value) -or ($tn -match 'שינוי רזולוציה' -and [Math]::Min($mi.Width, $mi.Height) -le 360)
                Rep $name ('tool ' + $ti + ' ' + $tn) $okBlock ('blocked: ' + $blocked); $dlg.Dispose(); continue
            }
            $sugg = $dlg.GetType().GetField('_out', $IN).GetValue($dlg).Text
            $o = Join-Path $Out ($tag + '-tool' + $ti + [IO.Path]::GetExtension($sugg))
            $job = $dlg.GetType().GetMethod('BuildJob', $IN).Invoke($dlg, @([string]$o)); $o = $job.OutputPath
            $combo = $dlg.GetType().GetField('_combo', $IN).GetValue($dlg)
            $slider = $dlg.GetType().GetField('_slider', $IN).GetValue($dlg)
            $opt = if ($combo) { $combo.Text } else { '' }
            $val = if ($slider) { $slider.Value } else { 0 }
            $dlg.Dispose()
            $sw = [Diagnostics.Stopwatch]::StartNew(); $err = RunJob $job
            $label = ('tool ' + $ti + ' ' + $tn)
            if ($err) { Rep $name $label $false $err; continue }
            if (-not (Test-Path $o) -or (Get-Item $o).Length -lt 100) { Rep $name $label $false 'no output'; continue }
            $mo = Probe $o
            $vs = Streams $mo 'video'; $as = Streams $mo 'audio'
            $d2 = $mo.DurationSec
            $ratio = (Get-Item $o).Length / [double](Get-Item $f).Length
            $note = ('{0:0}s  {1}  dur {2:0.00}  v{3} a{4}  {5}x{6}  {7:N0} KB  x{10:0.00}  opt="{8}" val={9}' -f $sw.Elapsed.TotalSeconds, [IO.Path]::GetExtension($o), $d2, $vs.Count, $as.Count, $mo.Width, $mo.Height, ((Get-Item $o).Length / 1KB), $opt, $val, $ratio)
            $good = $true
            $rangeSec = ($b - $a) / 1000.0
            if ($tool.UseRange -and $tn -notmatch 'תמונה') { if (-not (Near $d2 $rangeSec ([Math]::Max(0.8, $rangeSec * 0.1)))) { $good = $false; $note += '  !range' } }
            elseif ($tn -match 'מהירות') { if (-not (Near $d2 ($dur / [Math]::Max(0.1, $val)) ([Math]::Max(1.0, $dur * 0.03)))) { $good = $false; $note += '  !speed-duration' } }
            elseif ($tn -match 'חיבור') { if ($d2 -lt $dur + 1) { $good = $false; $note += '  !join-too-short' } }
            elseif ($tn -match 'תמונה') {
                # התמונה עצמה, לא Probe: ffmpeg -i על PNG לא מחזיר מידות שהתוכנה קוראת, וזה נרשם ״!no-image״ בכל סבב עד 8.10 -
                # אזעקת שווא קבועה שמלמדת להתעלם מאדום
                $iw = 0; try { $img = [Drawing.Image]::FromFile($o); $iw = $img.Width; $ih = $img.Height; $img.Dispose() } catch { }
                if ($iw -ne $mi.Width) { $good = $false; $note += '  !no-image' } else { $note += ('  image ' + $iw + 'x' + $ih) }
            }
            elseif (-not (Near $d2 $dur ([Math]::Max(1.0, $dur * 0.02)))) { $good = $false; $note += '  !duration' }
            if ($tn -match 'חילוץ הפסקול' -and ($vs.Count -gt 0 -or $as.Count -eq 0)) { $good = $false; $note += '  !audio-only' }
            if ($tn -match 'הסרת הקול' -and $as.Count -gt 0) { $good = $false; $note += '  !still-has-audio' }
            if ($tn -match 'סיבוב' -and $opt -match '90' -and $mo.Width -ne $mi.Height) { $good = $false; $note += '  !not-rotated' }
            # ״שינוי רזולוציה״ מקטין באמת (8.10: על 720p החלון נפתח על 480p, ולא מקודד 720p לקובץ כבד פי 2.6)
            if ($tn -match 'שינוי רזולוציה') { $hWant = [int]([regex]::Match($opt, '\d{3,4}').Value); if ([Math]::Min($mo.Width, $mo.Height) -ne $hWant -or [Math]::Min($mo.Width, $mo.Height) -ge [Math]::Min($mi.Width, $mi.Height)) { $good = $false; $note += '  !height' } }
            if ($tn -match 'וואטסאפ|לשליחה' -and [Math]::Min($mo.Width, $mo.Height) -gt 720) { $good = $false; $note += '  !over-720' }
            # לא גדול מהמקור: ״דחיסה״ תמיד, ״וואטסאפ״ כשהמקור לא גדול מ-720p (עד 8.10 שניהם יצאו גדולים מסרטון רגיל מהרשת)
            if ($tn -match 'דחיסה' -and $ratio -ge 1) { $good = $false; $note += '  !not-smaller' }
            if ($tn -match 'וואטסאפ|לשליחה' -and [Math]::Min($mi.Width, $mi.Height) -le 720 -and $ratio -gt 1) { $good = $false; $note += '  !bigger' }
            # ״שייכנס ל-X״ - בלי סובלנות: מי שצריך לעמוד במגבלה של מייל, 1% מעל זה כישלון
            if ($tn -match 'גודל קובץ' -and $val -gt 0 -and ((Get-Item $o).Length / 1MB) -gt $val) { $good = $false; $note += '  !over-target' }
            if ($tn -match 'ערוץ שמע' -and $as.Count -ne 1) { $good = $false; $note += '  !tracks' }
            if ($vid -and -not ($tn -match 'חילוץ הפסקול|תמונה') -and $vs.Count -eq 0 -and $o -notmatch '\.(mp3|wav|m4a)$') { $good = $false; $note += '  !lost-video' }
            Rep $name $label $good $note
        }
    }
}

# ---- כתוביות מוטמעות: שליפה ----
Write-Host ''
Write-Host '---- summary ----'
$csv = Join-Path $Out 'results.csv'
$results | Export-Csv -Encoding UTF8 -NoTypeInformation $csv
Write-Host ('{0} ok, {1} bad  ->  {2}' -f $script:ok, $script:bad, $csv)
