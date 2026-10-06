# test-subs.ps1 - קובצי כתוביות כמו שהם מסתובבים בעולם, דרך Formats.Load של
# התוכנה: קידודים (UTF-8 עם ובלי BOM, ‏UTF-16 בשני הכיוונים, חלונות-1255 ו-1256),
# תגיות עיצוב, ‏WebVTT עם הגדרות, ‏ASS מעוצב, ‏MicroDVD. בודק שהטקסט שחזר הוא
# הטקסט שנכתב, בלי שאריות תגיות - ושהשמירה חזרה משמרת אותו.
# **נולד מבאג** (24.9.2026): כל קידוד ישן נחשב עברית, ו״Déjà vu״ נפתח כ-״Dיjא vu״.
# צפוי: 26 בדיקות. הקבצים ב-%TEMP%\ss-sweep\subs
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
$dir = Join-Path $env:TEMP 'ss-sweep\subs'
New-Item -ItemType Directory -Force $dir | Out-Null
$ok = 0; $bad = 0
function Rep($what, $good, $detail) {
    if ($good) { $script:ok++ } else { $script:bad++ }
    Write-Host ('  {0} {1,-34} {2}' -f $(if ($good) { 'OK ' } else { 'BAD' }), $what, $detail) -ForegroundColor $(if ($good) { 'Gray' } else { 'Red' })
}
function Load($p) { return (TY 'Formats').GetMethod('Load', $ST).Invoke($null, @([string]$p)) }
function Texts($r) { return @($r.Cues | ForEach-Object { [string]$_.Text }) }

$he = @('שלום, זו כתובית ראשונה', 'שורה שנייה עם "מירכאות" ו-100%', 'אחרונה: נקודה.')
$srt = "1`r`n00:00:01,000 --> 00:00:03,000`r`n" + $he[0] + "`r`n`r`n2`r`n00:00:04,000 --> 00:00:06,500`r`n" + $he[1] + "`r`n`r`n3`r`n00:00:07,000 --> 00:00:09,000`r`n" + $he[2] + "`r`n"

function Check($name, [byte[]]$bytes, $ext, [string[]]$want, $wantEnc) {
    $p = Join-Path $dir (($name -replace '[<>{}\\/:*?"|]', '_') + $ext)
    [IO.File]::WriteAllBytes($p, $bytes)
    try { $r = Load $p } catch { Rep $name $false $_.Exception.InnerException.Message; return }
    $got = @(Texts $r)
    $same = ($got.Count -eq $want.Count)
    if ($same) { for ($i = 0; $i -lt $want.Count; $i++) { if (($got[$i] -replace "`r`n", "`n") -ne ($want[$i] -replace "`r`n", "`n")) { $same = $false } } }
    $encOk = (-not $wantEnc) -or ($r.Encoding -like $wantEnc)
    $shown = ($got | ForEach-Object { '«' + ($_ -replace "`r?`n", ' / ') + '»' }) -join ' '
    Rep $name ($same -and $encOk) ('{0} cues, enc={1}  {2}' -f $got.Count, $r.Encoding, $shown)
}

Write-Host '---- encodings ----'
$u8 = New-Object Text.UTF8Encoding $false
Check 'srt utf8 bom' ([byte[]](0xEF, 0xBB, 0xBF) + $u8.GetBytes($srt)) '.srt' $he 'UTF-8'
Check 'srt utf8' ($u8.GetBytes($srt)) '.srt' $he 'UTF-8'
Check 'srt utf8 LF only' ($u8.GetBytes($srt.Replace("`r`n", "`n"))) '.srt' $he $null
Check 'srt utf16le bom' ([Text.Encoding]::Unicode.GetPreamble() + [Text.Encoding]::Unicode.GetBytes($srt)) '.srt' $he 'UTF-16'
Check 'srt utf16be bom' ([Text.Encoding]::BigEndianUnicode.GetPreamble() + [Text.Encoding]::BigEndianUnicode.GetBytes($srt)) '.srt' $he 'UTF-16BE'
Check 'srt utf16le no bom' ([Text.Encoding]::Unicode.GetBytes($srt)) '.srt' $he 'UTF-16'
Check 'srt windows-1255' ([Text.Encoding]::GetEncoding(1255).GetBytes($srt)) '.srt' $he '*1255*'
$nik = @('בְּרֵאשִׁית בָּרָא', 'אֱלֹהִים', 'אֵת הַשָּׁמַיִם')
$srtN = $srt.Replace($he[0], $nik[0]).Replace($he[1], $nik[1]).Replace($he[2], $nik[2])
Check 'srt 1255 with niqqud' ([Text.Encoding]::GetEncoding(1255).GetBytes($srtN)) '.srt' $nik '*1255*'
$ar = @('مرحبا بكم', 'هذا سطر ثان', 'النهاية')
$srtA = $srt.Replace($he[0], $ar[0]).Replace($he[1], $ar[1]).Replace($he[2], $ar[2])
Check 'srt arabic windows-1256' ([Text.Encoding]::GetEncoding(1256).GetBytes($srtA)) '.srt' $ar $null
$ru = @('Привет всем', 'Вторая строка', 'Конец')
$srtR = $srt.Replace($he[0], $ru[0]).Replace($he[1], $ru[1]).Replace($he[2], $ru[2])
Check 'srt russian windows-1251' ([Text.Encoding]::GetEncoding(1251).GetBytes($srtR)) '.srt' $ru $null
$fr = @('Déjà vu, à côté', 'Ça va très bien', 'Fin.')
$srtF = $srt.Replace($he[0], $fr[0]).Replace($he[1], $fr[1]).Replace($he[2], $fr[2])
Check 'srt french windows-1252' ([Text.Encoding]::GetEncoding(1252).GetBytes($srtF)) '.srt' $fr $null

Write-Host '---- shapes and tags ----'
$tagged = "1`r`n00:00:01,000 --> 00:00:03,000`r`n<i>" + $he[0] + "</i>`r`n`r`n2`r`n00:00:04,000 --> 00:00:06,500`r`n<font color=`"#ffff00`">" + $he[1] + "</font>`r`n`r`n3`r`n00:00:07,000 --> 00:00:09,000`r`n{\an8}" + $he[2] + "`r`n"
Check 'srt with <i> <font> {\an8}' ($u8.GetBytes($tagged)) '.srt' $he $null
$two = "1`r`n00:00:01,000 --> 00:00:03,000`r`nשורה ראשונה`r`nשורה שנייה`r`n`r`n"
Check 'srt two lines' ($u8.GetBytes($two)) '.srt' @("שורה ראשונה`r`nשורה שנייה") $null
$messy = "`r`n`r`n1`r`n00:00:01.000 --> 00:00:03.000`r`n" + $he[0] + "`r`n2`r`n00:00:04,000 --> 00:00:06,500`r`n" + $he[1] + "`r`n`r`n`r`n`r`n7`r`n00:00:07,000-->00:00:09,000`r`n" + $he[2] + "  `r`n"
Check 'srt messy (dots, no blank, gaps)' ($u8.GetBytes($messy)) '.srt' $he $null
$vtt = "WEBVTT`r`nKind: captions`r`nLanguage: he`r`n`r`nSTYLE`r`n::cue { color: yellow }`r`n`r`nNOTE this is a comment`r`n`r`nintro`r`n00:01.000 --> 00:03.000 align:start position:10%`r`n<v Speaker>" + $he[0] + "</v>`r`n`r`n00:00:04.000 --> 00:00:06.500 line:0`r`n<c.yellow>" + $he[1] + "</c>`r`n`r`n00:00:07.000 --> 00:00:09.000`r`n<00:00:07.500>" + $he[2] + "`r`n"
Check 'vtt with style, note, tags' ($u8.GetBytes($vtt)) '.vtt' $he $null
$ass = "[Script Info]`r`nScriptType: v4.00+`r`nPlayResX: 1920`r`nPlayResY: 1080`r`n`r`n[V4+ Styles]`r`nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding`r`nStyle: Default,Arial,60,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,40,1`r`n`r`n[Events]`r`nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text`r`nComment: 0,0:00:00.50,0:00:00.90,Default,,0,0,0,,הערה שלא אמורה להופיע`r`nDialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,{\b1}" + $he[0] + "{\b0}`r`nDialogue: 0,0:00:04.00,0:00:06.50,Default,Actor,0,0,0,,{\pos(960,100)}" + $he[1] + "`r`nDialogue: 0,0:00:07.00,0:00:09.00,Default,,0,0,0,," + $he[2] + "`r`n"
Check 'ass with styles, comment, tags' ($u8.GetBytes($ass)) '.ass' $he $null
$ass2 = $ass.Replace('{\pos(960,100)}' + $he[1], 'שורה א\Nשורה ב')
Check 'ass \N line break' ($u8.GetBytes($ass2)) '.ass' @($he[0], "שורה א`r`nשורה ב", $he[2]) $null
$sub = "{1}{1}25.000`r`n{25}{75}" + $he[0] + "`r`n{100}{162}" + $he[1] + "`r`n{175}{225}" + $he[2] + "`r`n"
Check 'microdvd with fps line' ($u8.GetBytes($sub)) '.sub' $he $null

Write-Host '---- save and load back ----'
$src = Join-Path $dir 'srt utf8.srt'
$r = Load $src
foreach ($fmt in 'Srt', 'Vtt', 'Ass') {
    $fe = [Enum]::Parse((TY 'SubFormat'), $fmt)
    $o = Join-Path $dir ('roundtrip.' + $fmt.ToLower())
    $style = [Activator]::CreateInstance((TY 'SubStyle'))
    $argv = New-Object object[] 7; $argv[0] = [string]$o; $argv[1] = $r.Cues; $argv[2] = $fe; $argv[3] = $style; $argv[4] = 1920; $argv[5] = 1080; $argv[6] = $true
    (TY 'Formats').GetMethod('Save', $ST).Invoke($null, $argv)
    $back = Load $o
    $bt = Texts $back
    $same = $bt.Count -eq 3 -and $bt[0] -eq $he[0] -and $bt[1] -eq $he[1] -and $bt[2] -eq $he[2]
    $times = @($back.Cues | ForEach-Object { '{0}-{1}' -f $_.Start, $_.End }) -join ' '
    Rep ('roundtrip ' + $fmt) ($same -and $times -eq '1000-3000 4000-6500 7000-9000') $times
}

# ---- צריבה: סדר המילים בשורה עברית עם אנגלית (0.8.2) ----
# עד 0.8.2 הסגנון נכתב עם Encoding=177, ו-libass כופה אז בסיס משמאל לימין: ״לורד אלטמן מבית
# Open AI.״ נצרב עם ״Open AI.״ בקצה הימני (נמצא על סרטון אמיתי). צורבים את מה שהתוכנה מייצרת,
# ומשווים לייחוס שנכון בכל מקרה: כל שורה עטופה ב-RLE...PDF. ובקרה: עם 177 הישן - חייב להיות שונה.
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$ffx = [string](TY 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
$cueT = TY 'Cue'
$list = [Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($cueT))
foreach ($ln in @('לורד אלטמן מבית Open AI.', 'הוא לא בונה AGI, הוא גונב לי פיצ''רים.', 'Open AI מבקש מיליון GPU, ואני אספק AGI השנה.')) {
    $list.Add([Activator]::CreateInstance($cueT, @([int64]0, [int64]5000, [string]$ln)))
}
$appAss = [string](TY 'Formats').GetMethod('ToAss', $ST).Invoke($null, @($list, [Activator]::CreateInstance((TY 'SubStyle')), [int]1280, [int]720))
$rle = [string][char]0x202B; $pdf = [string][char]0x202C
$refAss = [regex]::Replace($appAss, '(?m)^(Style: .*),[^,\r\n]*(\r?)$', '$1,177$2')
$refAss = [regex]::Replace($refAss, '(?m)^(Dialogue: (?:[^,]*,){9})(.*)$', { param($m) $m.Groups[1].Value + $rle + $m.Groups[2].Value.TrimEnd("`r") + $pdf })
$oldAss = [regex]::Replace($appAss, '(?m)^(Style: .*),[^,\r\n]*(\r?)$', '$1,177$2')
function BurnAss($name, $text) {
    [IO.File]::WriteAllText((Join-Path $dir "$name.ass"), $text, (New-Object Text.UTF8Encoding $true))
    Push-Location $dir
    & $ffx -hide_banner -loglevel error -y -f lavfi -i 'color=c=0x404850:s=1280x720:d=1' -vf "subtitles=$name.ass" -frames:v 1 "$name.png"
    Pop-Location
    return (Join-Path $dir "$name.png")
}
function PixDiff($a, $b) {   # לא Diff: זה כינוי מובנה של Compare-Object, והוא גובר על פונקציה
    $x = [Drawing.Bitmap]::FromFile($a); $y = [Drawing.Bitmap]::FromFile($b); $n = 0
    for ($j = 0; $j -lt $x.Height; $j += 2) { for ($i = 0; $i -lt $x.Width; $i += 2) {
        $p = $x.GetPixel($i, $j); $q = $y.GetPixel($i, $j)
        if ([Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B) -gt 90) { $n++ } } }
    $x.Dispose(); $y.Dispose(); return $n
}
$pa = BurnAss 'bidi-app' $appAss; $pr = BurnAss 'bidi-ref' $refAss; $po = BurnAss 'bidi-old' $oldAss
$dApp = PixDiff $pa $pr; $dOld = PixDiff $po $pr
Rep 'burn: Hebrew+English word order' ($dApp -lt 40) ("differs from the reference in $dApp sampled pixels")
Rep 'burn: the old style would fail here' ($dOld -gt 400) ("old Encoding=177 differs in $dOld")
Rep 'burn: style asks libass to detect direction' ($appAss -match '(?m)^Style: .*,-1\r?$') ''

# ---- ערוץ נפרד בקובץ שכבר יש בו כתוביות (0.8.5) ----
# עד 0.8.5 ״לשמור גם ערוצי כתוביות שכבר קיימים״ כתב את השפה, השם ו״ברירת מחדל״ על הערוץ הישן
# (‏s:0), והחדש יצא בלי שם ובלי סימון; וכל ערוץ ישן קודד לפורמט שלנו (ערוץ תמונה הפיל את הפעולה).
function ProbeIt($p) { return (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$p)) }
$oldSrt = Join-Path $dir 'old-track.srt'
[IO.File]::WriteAllText($oldSrt, "1`r`n00:00:00,500 --> 00:00:02,000`r`nOld English track`r`n", $u8)
foreach ($ext in '.mkv', '.mp4') {
    $base = Join-Path $dir ('with-track' + $ext)
    $sc = if ($ext -eq '.mkv') { 'ass' } else { 'mov_text' }
    & $ffx -nostdin -hide_banner -loglevel error -y -f lavfi -i 'color=c=0x404850:s=320x240:d=3' -f lavfi -i 'sine=d=3' -i $oldSrt -map 0 -map 1 -map 2 -c:v libx264 -c:a aac -c:s $sc -metadata:s:s:0 language=eng -disposition:s:0 default -t 3 $base
    $mi = ProbeIt $base
    $doc = [Activator]::CreateInstance((TY 'Doc'))
    $dc = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
    $dc.Add([Activator]::CreateInstance($cueT, @([int64]500, [int64]2500, [string]'כתובית חדשה')))
    $dlg = [Activator]::CreateInstance((TY 'ExportVideoDlg'), @($null, $doc, $mi, [Activator]::CreateInstance((TY 'SubStyle')), [long]-1, [long]-1))
    [void]$dlg.GetType().GetMethod('SetMode', $IN).Invoke($dlg, @($false))
    $dlg.GetType().GetField('_keepExisting', $IN).GetValue($dlg).Checked = $true
    $o = Join-Path $dir ('soft-out' + $ext)
    if ([IO.File]::Exists($o)) { [IO.File]::Delete($o) }
    $job = $dlg.GetType().GetMethod('BuildJob', $IN).Invoke($dlg, @([string]$o)); $dlg.Dispose()
    $argv = New-Object object[] 5; $argv[0] = $ffx; $argv[1] = [string]$job.Args; $argv[4] = [string]$job.WorkDir
    [void](TY 'Ff').GetMethod('RunSync', $ST).Invoke($null, $argv)
    $subs = @((ProbeIt $job.OutputPath).Streams | Where-Object { $_.Type -eq 'subtitle' })
    $mine = if ($subs.Count -gt 0) { $subs[$subs.Count - 1] } else { $null }
    # ‏MP4 שומר את השם כ-handler, ו-ffmpeg לא מחזיר אותו כ-title - השם נבדק רק ב-MKV
    $good = $subs.Count -eq 2 -and $mine.Language -eq 'heb' -and ($ext -ne '.mkv' -or $mine.Title -eq 'עברית') -and $mine.Default -and -not $subs[0].Default -and $subs[0].Language -eq 'eng'
    Rep ('soft track beside an old one ' + $ext) $good (($subs | ForEach-Object { '{0}:{1}:{2}:{3}' -f $_.Codec, $_.Language, $_.Title, $(if ($_.Default) { 'default' } else { '-' }) }) -join '  ')
}
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $ok, $bad)
if ($bad -gt 0) { exit 1 }
