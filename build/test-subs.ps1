# test-subs.ps1 - קובצי כתוביות כמו שהם מסתובבים בעולם, דרך Formats.Load של
# התוכנה: קידודים (UTF-8 עם ובלי BOM, ‏UTF-16 בשני הכיוונים, חלונות-1255 ו-1256),
# תגיות עיצוב, ‏WebVTT עם הגדרות, ‏ASS מעוצב, ‏MicroDVD. בודק שהטקסט שחזר הוא
# הטקסט שנכתב, בלי שאריות תגיות - ושהשמירה חזרה משמרת אותו.
# **נולד מבאג** (24.9.2026): כל קידוד ישן נחשב עברית, ו״Déjà vu״ נפתח כ-״Dיjא vu״.
# צפוי: 35 בדיקות. הקבצים ב-%TEMP%\ss-sweep\subs
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

Write-Host '---- save keeps the old encoding (0.8.29) ----'
# SRT בקידוד הישן של חלונות - טלוויזיות ונגנים ישנים קוראים רק אותו. עד 0.8.29 כל שמירה הפכה אותו ל-UTF-8.
$src1255 = Join-Path $dir 'srt windows-1255.srt'
$r1255 = Load $src1255
$keepM = (TY 'Formats').GetMethod('SaveKeepingEncoding', $ST)
function SaveKeep($cues, $path, $enc) {
    $a = New-Object object[] 8; $a[0] = [string]$path; $a[1] = $cues; $a[2] = [Enum]::Parse((TY 'SubFormat'), 'Srt'); $a[3] = [Activator]::CreateInstance((TY 'SubStyle')); $a[4] = 1920; $a[5] = 1080; $a[6] = [string]$enc
    [void]$keepM.Invoke($null, $a); return $a[7]
}
$o1 = Join-Path $dir 'kept-1255.srt'; $used = SaveKeep $r1255.Cues $o1 $r1255.Encoding
$b1 = [IO.File]::ReadAllBytes($o1)
$back1 = [Text.Encoding]::GetEncoding(1255).GetString($b1)
Rep 'save: a Windows-1255 file stays Windows-1255 (no BOM), and says so' ($used -like '*1255*' -and $b1[0] -ne 0xEF -and $back1.Contains($he[0]) -and $back1.Contains($he[2])) ("used=" + $used)
$r1255.Cues[0].Text = 'שלום 🙂 עם סמל'
$o2 = Join-Path $dir 'kept-emoji.srt'; $used2 = SaveKeep $r1255.Cues $o2 $r1255.Encoding
$b2 = [IO.File]::ReadAllBytes($o2)
Rep 'save: a character that 1255 cannot hold - UTF-8 with BOM, nothing lost' ($used2 -eq $null -and $b2[0] -eq 0xEF -and [Text.Encoding]::UTF8.GetString($b2).Contains('🙂')) ''

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

# ---- התצוגה = הצריבה (0.8.28; המדידה המלאה: ``hp-tools\style-compare.ps1``) ----
# עד 0.8.28: ״רקע מלא״ נצרב כקופסה שחורה אטומה לכל שורה, והתצוגה הראתה רקע שקוף-למחצה; ושורה רחבה מהמסך נשברה בצריבה
# לשתיים ונחתכה בתצוגה. צורבים פריים ומציירים אותו ב-SubStyle.Render, ומשווים את המלבן של מה שאינו רקע.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class SubsInk {
  public static int[] Box(Bitmap b, Color r) {
    BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    byte[] buf = new byte[d.Stride * b.Height]; Marshal.Copy(d.Scan0, buf, 0, buf.Length); b.UnlockBits(d);
    int l = int.MaxValue, t = int.MaxValue, rr = -1, bb = -1;
    for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) {
      int i = y * d.Stride + x * 4;
      if (Math.Abs(buf[i] - r.B) + Math.Abs(buf[i + 1] - r.G) + Math.Abs(buf[i + 2] - r.R) <= 40) continue;
      if (x < l) l = x; if (x > rr) rr = x; if (y < t) t = y; if (y > bb) bb = y;
    }
    return new int[] { l, t, rr, bb };
  }
}
"@
function SameAsBurn($name, $setup, [string]$text) {
    $sty = [Activator]::CreateInstance((TY 'SubStyle')); & $setup $sty
    $one = [Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($cueT))
    $one.Add([Activator]::CreateInstance($cueT, @([int64]0, [int64]5000, $text)))
    $argv = New-Object object[] 4; $argv[0] = $one; $argv[1] = $sty; $argv[2] = [int]1280; $argv[3] = [int]720
    $ass = [string](TY 'Formats').GetMethod('ToAss', $ST).Invoke($null, $argv)
    $png = BurnAss $name $ass
    $raw = [Drawing.Image]::FromFile($png); $burn = New-Object Drawing.Bitmap $raw; $raw.Dispose()
    $bg = $burn.GetPixel(2, 2)
    $prev = New-Object Drawing.Bitmap 1280, 720; $g = [Drawing.Graphics]::FromImage($prev); $g.Clear($bg)
    $sty.Render($g, (New-Object Drawing.RectangleF 0, 0, 1280, 720), $text); $g.Dispose()
    $a = [SubsInk]::Box($burn, $bg); $b = [SubsInk]::Box($prev, $bg); $burn.Dispose(); $prev.Dispose()
    $worst = 0; for ($k = 0; $k -lt 4; $k++) { $worst = [Math]::Max($worst, [Math]::Abs($a[$k] - $b[$k])) }
    return [pscustomobject]@{ Worst = $worst; Burn = ($a -join ','); Prev = ($b -join ','); Ass = $ass }
}
$light = SameAsBurn 'same-lightbox' { param($s) $s.OpaqueBox = $true; $s.BoxColor = [Drawing.Color]::FromArgb(128, 255, 255, 255); $s.Primary = [Drawing.Color]::FromArgb(20, 20, 30); $s.OutlineWidth = 0.0 } "זו דוגמה לכתובית`nבשתי שורות"
Rep 'preview = burn: light half-transparent background, one box behind both lines' ($light.Worst -le 3) ("burn " + $light.Burn + " / preview " + $light.Prev)
Rep 'background: BorderStyle 4, its colour and alpha in BackColour, outline kept' ($light.Ass -match '(?m)^Style: Main,[^,]*,\d+,&H00[0-9A-F]{6},&H[0-9A-F]{8},&H00000000,&H7FFFFFFF,(?:[^,]*,){8}4,') ''
$boxSt = [Activator]::CreateInstance((TY 'SubStyle')); $boxSt.OpaqueBox = $true; $boxSt.BoxColor = [Drawing.Color]::FromArgb(128, 255, 255, 255)
$forPlayers = [string]$boxSt.ToAssStyleLineForPlayers(720)
Rep 'saved .ass / separate track: background as BorderStyle 3 (every player knows it; 4 is new libass only)' ($forPlayers -match '^Style: Main,[^,]*,\d+,&H00[0-9A-F]{6},&H[0-9A-F]{8},&H7FFFFFFF,&HFF[0-9A-F]{6},(?:[^,]*,){8}3,') $forPlayers
$wide = SameAsBurn 'same-wide' { param($s) $s.FontPct = 8.0 } 'זו שורה ארוכה מאוד שלא נכנסת ברוחב המסך כשהגופן גדול, וצריך לראות מה קורה לה בצריבה ובתצוגה'
Rep 'preview = burn: a line wider than the screen wraps the same way' ($wide.Worst -le 8) ("burn " + $wide.Burn + " / preview " + $wide.Prev)
# ---- טשטוש אזור (0.8.31): הצריבה דרך המסלול של החלון (Burn.VideoFilterArgs), מול התצוגה (SubStyle.RenderBlur) ----
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class SubsMad {
  static byte[] Px(Bitmap b, out int st) { BitmapData d = b.LockBits(new Rectangle(0,0,b.Width,b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb); st = d.Stride; byte[] x = new byte[d.Stride*b.Height]; Marshal.Copy(d.Scan0, x, 0, x.Length); b.UnlockBits(d); return x; }
  public static double Mad(Bitmap a, Bitmap b, Rectangle r) { int sa, sb; byte[] pa = Px(a, out sa), pb = Px(b, out sb); double s = 0; long n = 0;
    for (int y = r.Top; y < r.Bottom; y++) for (int x = r.Left; x < r.Right; x++) { int i = y*sa+x*4, j = y*sb+x*4; s += Math.Abs(pa[i]-pb[j]) + Math.Abs(pa[i+1]-pb[j+1]) + Math.Abs(pa[i+2]-pb[j+2]); n += 3; }
    return n > 0 ? s / n : 0; }
}
"@
$IN2 = [Reflection.BindingFlags]'NonPublic,Public,Instance'
$blurSt = [Activator]::CreateInstance((TY 'SubStyle')); $blurSt.BlurOn = $true; $blurSt.BlurPct = 70; $blurSt.BlurArea = New-Object Drawing.RectangleF 0.1, 0.6, 0.8, 0.3
$bl = [Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($cueT)); $bl.Add([Activator]::CreateInstance($cueT, @([int64]1000, [int64]2000, 'כתובית')))
Push-Location $dir; & $ffx -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=s=640x360:d=4:r=10' -frames:v 1 -ss 1.5 blur-src.png; Pop-Location
function BurnBlur($name, $onlyCue, $at) {
    $blurSt.BlurOnlyWithCue = $onlyCue
    $assName = "$name.ass"; [IO.File]::WriteAllText((Join-Path $dir $assName), "[Script Info]`r`nScriptType: v4.00+`r`n", (New-Object Text.UTF8Encoding $true))
    $vfa = [string](TY 'Burn').GetMethod('VideoFilterArgs', $ST).Invoke($null, [object[]]@($bl, $blurSt.psobject.BaseObject, [int]640, [int]360, [int64]0, $assName, [string]$dir, [string]''))
    Push-Location $dir
    $cmd = "-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=640x360:d=4:r=10 " + $vfa + "-ss $at -frames:v 1 $name.png"
    $p = Start-Process $ffx -ArgumentList $cmd -NoNewWindow -Wait -PassThru
    Pop-Location
    $raw = [Drawing.Image]::FromFile((Join-Path $dir "$name.png")); $b = New-Object Drawing.Bitmap $raw; $raw.Dispose(); return @($b, $vfa)
}
$srcRaw = [Drawing.Image]::FromFile((Join-Path $dir 'blur-src.png')); $bsrc = New-Object Drawing.Bitmap $srcRaw; $srcRaw.Dispose()
$area = $blurSt.GetType().GetMethod('BlurPixels', $IN2).Invoke($blurSt.psobject.BaseObject, @([int]640, [int]360))
$inner = New-Object Drawing.Rectangle ($area.X + 2), ($area.Y + 2), ($area.Width - 4), ($area.Height - 4)
$above = New-Object Drawing.Rectangle 0, 0, 640, ($area.Y - 4)
$r1 = BurnBlur 'blur-always' $false 1.5; $burnA = $r1[0]
$prev = New-Object Drawing.Bitmap 640, 360; $gp = [Drawing.Graphics]::FromImage($prev); $gp.DrawImage($bsrc, 0, 0, 640, 360)
$blurSt.RenderBlur($gp, (New-Object Drawing.RectangleF 0, 0, 640, 360), $bsrc); $gp.Dispose()
$effect = [SubsMad]::Mad($burnA, $bsrc, $inner); $gap = [SubsMad]::Mad($prev, $burnA, $inner); $out = [SubsMad]::Mad($burnA, $bsrc, $above)
Rep 'blur: the burned area is blurred, the rest untouched, and the preview matches it' ($effect -gt 8 -and $gap -lt $effect * 0.35 -and $out -lt 2) ("effect {0:0.0}, preview gap {1:0.0}, outside {2:0.0}; {3}" -f $effect, $gap, $out, $r1[1].Trim())
$on = (BurnBlur 'blur-cue-on' $true 1.5)[0]; $off = (BurnBlur 'blur-cue-off' $true 2.6)[0]
Push-Location $dir; & $ffx -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=s=640x360:d=4:r=10' -ss 2.6 -frames:v 1 blur-src2.png; Pop-Location
$s2Raw = [Drawing.Image]::FromFile((Join-Path $dir 'blur-src2.png')); $bsrc2 = New-Object Drawing.Bitmap $s2Raw; $s2Raw.Dispose()
$eOn = [SubsMad]::Mad($on, $bsrc, $inner); $eOff = [SubsMad]::Mad($off, $bsrc2, $inner)
Rep 'blur only with a subtitle: blurred at 1.5s (subtitle 1-2s), untouched at 2.6s' ($eOn -gt 8 -and $eOff -lt 2) ("{0:0.0} / {1:0.0}" -f $eOn, $eOff)
foreach ($b in @($burnA, $prev, $on, $off, $bsrc, $bsrc2)) { $b.Dispose() }

# ---- אזורי צנזורה לפי זמן (0.8.34, ‏2כט): כל אזור משנה את התמונה רק בזמן שלו, והעיגול לא נוגע בפינות של המלבן ----
$zT = TY 'BlurZone'
function Zone($x, $y, $w, $h, $from, $to, $look, $ell) { $z = [Activator]::CreateInstance($zT); $z.Area = New-Object Drawing.RectangleF $x, $y, $w, $h; $z.FromMs = [long]$from; $z.ToMs = [long]$to; $z.Look = $look; $z.Ellipse = $ell; return $z }
$zs = [Activator]::CreateInstance((TY 'SubStyle'))
$zs.Zones.Add((Zone 0.1 0.1 0.3 0.3 1000 2000 0 $false)); $zs.Zones.Add((Zone 0.6 0.55 0.3 0.4 -1 -1 2 $true)); $zs.Zones.Add((Zone 0.1 0.6 0.3 0.3 2500 -1 1 $false))
$zAss = 'zones.ass'; [IO.File]::WriteAllText((Join-Path $dir $zAss), "[Script Info]`r`nScriptType: v4.00+`r`n", (New-Object Text.UTF8Encoding $true))
$zArgs = [string](TY 'Burn').GetMethod('VideoFilterArgs', $ST).Invoke($null, [object[]]@($bl, $zs.psobject.BaseObject, [int]640, [int]360, [int64]0, $zAss, [string]$dir, [string]''))
function ZFrame($at, $name, [bool]$src) {
    Push-Location $dir
    $vf = if ($src) { '' } else { $zArgs }
    $cmd = "-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=640x360:d=4:r=10 " + $vf + "-ss $at -frames:v 1 $name.png"
    $p = Start-Process $ffx -ArgumentList $cmd -NoNewWindow -Wait -PassThru; Pop-Location
    $raw = [Drawing.Image]::FromFile((Join-Path $dir "$name.png")); $b = New-Object Drawing.Bitmap $raw; $raw.Dispose(); return $b
}
$s15 = ZFrame 1.5 'z-src15' $true; $b15 = ZFrame 1.5 'z-b15' $false; $s30 = ZFrame 3.0 'z-src30' $true; $b30 = ZFrame 3.0 'z-b30' $false
$rA = New-Object Drawing.Rectangle 70, 42, 180, 100; $rC = New-Object Drawing.Rectangle 70, 222, 180, 100
$bc = $b15.GetPixel(480, 270); $corner = [SubsMad]::Mad($b15, $s15, (New-Object Drawing.Rectangle 386, 200, 10, 10))
$zA15 = [SubsMad]::Mad($b15, $s15, $rA); $zA30 = [SubsMad]::Mad($b30, $s30, $rA); $zC15 = [SubsMad]::Mad($b15, $s15, $rC); $zC30 = [SubsMad]::Mad($b30, $s30, $rC)
Rep 'censor zones: each only in its time (blur 1-2s, pixels from 2.5s), black circle covers the centre and leaves the corners' ($zA15 -gt 4 -and $zA30 -lt 2 -and $zC15 -lt 2 -and $zC30 -gt 8 -and ($bc.R + $bc.G + $bc.B) -lt 30 -and $corner -lt 2) ("blur {0:0.0}/{1:0.0}  pixels {2:0.0}/{3:0.0}  centre rgb {4} corner {5:0.0}" -f $zA15, $zA30, $zC15, $zC30, ($bc.R + $bc.G + $bc.B), $corner)
foreach ($b in @($s15, $b15, $s30, $b30)) { $b.Dispose() }
# והתצוגה בנגן (`RenderZones`): אותו אזור שחור בעיגול - רק בזמן שלו, ומחוץ לעיגול בלי שינוי
$pz = [Activator]::CreateInstance((TY 'SubStyle')); $pz.Zones.Add((Zone 0.25 0.25 0.5 0.5 0 1000 2 $true))
function PreviewAt([long]$ms) { $fr = New-Object Drawing.Bitmap 320, 180; $gf = [Drawing.Graphics]::FromImage($fr); $gf.Clear([Drawing.Color]::FromArgb(200, 120, 40)); $gf.Dispose(); $out = New-Object Drawing.Bitmap 320, 180; $go = [Drawing.Graphics]::FromImage($out); $go.DrawImage($fr, 0, 0, 320, 180); $pz.RenderZones($go, (New-Object Drawing.RectangleF 0, 0, 320, 180), $fr, $ms); $go.Dispose(); $fr.Dispose(); return $out }
$pIn = PreviewAt 500; $pOut = PreviewAt 1500
$cIn = $pIn.GetPixel(160, 90); $cornerIn = $pIn.GetPixel(84, 48); $cOut = $pOut.GetPixel(160, 90)
Rep 'censor zones in the preview: black circle only in its time, corners outside the circle untouched' (($cIn.R + $cIn.G + $cIn.B) -lt 30 -and $cornerIn.R -gt 150 -and $cOut.R -gt 150) ("in {0}/{1}/{2}, corner R {3}, after R {4}" -f $cIn.R, $cIn.G, $cIn.B, $cornerIn.R, $cOut.R)
$pIn.Dispose(); $pOut.Dispose()

# גופן בלי עברית: התצוגה והצריבה מחליפות כל אחת לגופן אחר - בכתוביות עבריות החלון לא מציע אותו
$hh = (TY 'Fonts').GetMethod('HasHebrew', $ST)
$sdlg = [Activator]::CreateInstance((TY 'StyleDlg'), @([Activator]::CreateInstance((TY 'SubStyle')), $null, $true))
$fontItems = (TY 'StyleDlg').GetField('_font', [Reflection.BindingFlags]'NonPublic,Instance').GetValue($sdlg).Items
$fontNames = @(for ($k = 0; $k -lt $fontItems.Count; $k++) { [string]$fontItems[$k] }); $sdlg.Dispose()
Rep 'fonts: Hebrew in Arial, not in Consolas or a symbol font; the style window offers only Hebrew fonts' ($hh.Invoke($null, @('Arial')) -and -not $hh.Invoke($null, @('Consolas')) -and -not $hh.Invoke($null, @('Wingdings')) -and ($fontNames -contains 'Arial') -and -not ($fontNames -contains 'Consolas')) ("{0} fonts" -f $fontNames.Count)

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

# ---- 4K: ״להקטין ל-1080p״ (0.8.33) - רק כשהמקור גדול מ-1080p; הכתוביות והטשטוש במידות המקור, ואז הקטנה ----
$src4 = Join-Path $dir 'big-4k.mp4'
& $ffx -nostdin -hide_banner -loglevel error -y -f lavfi -i 'color=c=0x405060:s=3840x2160:d=1:r=10' -c:v libx264 -preset ultrafast -pix_fmt yuv420p $src4
function Burn4k($mi, [bool]$down, $name) {
    $doc = [Activator]::CreateInstance((TY 'Doc'))
    (TY 'Doc').GetField('Cues', $IN).GetValue($doc).Add([Activator]::CreateInstance($cueT, @([int64]0, [int64]900, [string]'כתובית')))
    $st4 = [Activator]::CreateInstance((TY 'SubStyle')); $st4.BlurOn = $true
    $dlg = [Activator]::CreateInstance((TY 'ExportVideoDlg'), @($null, $doc, $mi, $st4, [long]-1, [long]-1))
    $dlg.GetType().GetField('_quality', $IN).GetValue($dlg).SelectedIndex = 2
    $tg = $dlg.GetType().GetField('_down', $IN).GetValue($dlg); $tg.Checked = $down
    $big = [bool]$dlg.GetType().GetField('_big', $IN).GetValue($dlg)
    $o = Join-Path $dir $name; if ([IO.File]::Exists($o)) { [IO.File]::Delete($o) }
    $job = $dlg.GetType().GetMethod('BuildJob', $IN).Invoke($dlg, @([string]$o)); $dlg.Dispose()
    $argv = New-Object object[] 5; $argv[0] = $ffx; $argv[1] = [string]$job.Args; $argv[4] = [string]$job.WorkDir
    [void](TY 'Ff').GetMethod('RunSync', $ST).Invoke($null, $argv)
    $out = ProbeIt $job.OutputPath
    return @($big, $out.Width, $out.Height)
}
$mi4 = ProbeIt $src4
$r4 = Burn4k $mi4 $true 'big-down.mp4'; $r4b = Burn4k $mi4 $false 'big-full.mp4'
$small = Burn4k (ProbeIt (Join-Path $dir 'with-track.mkv')) $true 'small-down.mp4'
Rep '4K: "shrink to 1080p" is offered, burns 1920x1080 (with blur), and off keeps 3840x2160; not offered for a small video' ($r4[0] -and $r4[1] -eq 1920 -and $r4[2] -eq 1080 -and $r4b[1] -eq 3840 -and -not $small[0] -and $small[2] -eq 240) ("on {0}x{1}, off {2}x{3}, small offered={4} {5}x{6}" -f $r4[1], $r4[2], $r4b[1], $r4b[2], $small[0], $small[1], $small[2])

# ---- החלון ״טשטוש וצנזורה״ (0.8.35, ‏2כט ב): מה שמשתמש עושה - גרירה על התמונה, הזזה, פינה, ״התחלה כאן״ / ״סוף כאן״, ביטול, שמירה ----
$zmi = ProbeIt (Join-Path $dir 'with-track.mkv')
$orig = [Activator]::CreateInstance((TY 'SubStyle')); $orig.Zones.Add((Zone 0.05 0.05 0.1 0.1 -1 -1 2 $false))
$zargv = New-Object object[] 3; $zargv[1] = $zmi; $zargv[2] = $orig.Zones
$zd = [Activator]::CreateInstance((TY 'ZonesDlg'), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $zargv, $null)
$cv = $zd.GetType().GetField('_canvas', $IN).GetValue($zd)
$cT = $cv.GetType()
function Mouse($m, $rx, $ry) { $vr = $cv.VideoRect(); $e = [Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 1, [int]($vr.X + $rx * $vr.Width), [int]($vr.Y + $ry * $vr.Height), 0); [void]$cT.GetMethod($m, $IN).Invoke($cv, @($e)) }
function Drag($x1, $y1, $x2, $y2) { Mouse 'OnMouseDown' $x1 $y1; Mouse 'OnMouseMove' (($x1 + $x2) / 2) (($y1 + $y2) / 2); Mouse 'OnMouseMove' $x2 $y2; Mouse 'OnMouseUp' $x2 $y2 }
function Near($a, $b) { return [Math]::Abs($a - $b) -lt 0.02 }
$zdT = $zd.GetType()
function ZP($n) { $v = $zdT.GetProperty($n, $IN).GetValue($zd); return ,$v }
$zl = ZP 'Zones'
Drag 0.2 0.2 0.6 0.7
$nz = if ($zl.Count -eq 2) { $zl[1] } else { $null }
Rep 'zones window: dragging on the picture makes an area just there, from the start = the whole video' ($nz -ne $null -and (Near $nz.Area.X 0.2) -and (Near $nz.Area.Y 0.2) -and (Near $nz.Area.Width 0.4) -and (Near $nz.Area.Height 0.5) -and $nz.FromMs -eq -1 -and $nz.ToMs -eq -1 -and (ZP 'Selected') -eq 1) ("{0} areas, {1}" -f $zl.Count, $(if ($nz) { "{0:0.00},{1:0.00} {2:0.00}x{3:0.00} {4}..{5}" -f $nz.Area.X, $nz.Area.Y, $nz.Area.Width, $nz.Area.Height, $nz.FromMs, $nz.ToMs }))
Drag 0.4 0.45 0.5 0.45
$movedX = $nz.Area.X
Drag 0.7 0.7 0.9 0.95
Rep 'zones window: dragging an area moves it, its corner resizes it' ((Near $movedX 0.3) -and (Near $nz.Area.Right 0.9) -and (Near $nz.Area.Bottom 0.95) -and (Near $nz.Area.X 0.3)) ("moved x {0:0.00}; now {1:0.00},{2:0.00} to {3:0.00},{4:0.00}" -f $movedX, $nz.Area.X, $nz.Area.Y, $nz.Area.Right, $nz.Area.Bottom)
function ZCall($m, $a) { [void]$zdT.GetMethod($m, $IN).Invoke($zd, $a) }
ZCall 'Seek' @([long]1000); ZCall 'MarkHere' @($true); ZCall 'Seek' @([long]2500); ZCall 'MarkHere' @($false)
$w1 = (ZP 'WhenText'); $t1 = "{0}..{1}" -f $nz.FromMs, $nz.ToMs
ZCall 'Seek' @([long]500); ZCall 'MarkHere' @($false)
$refused = $nz.ToMs -eq 2500 -and (ZP 'WhenText') -ne $w1
ZCall 'Undo' @()
$nz2 = (ZP 'Zones')[1]
Rep 'zones window: "start here" / "end here" follow the player; an end before the start is refused with a note; Ctrl+Z undoes' ($t1 -eq '1000..2500' -and $w1 -match '00:01\.0' -and $w1 -match '00:02\.5' -and $refused -and $nz2.FromMs -eq 1000 -and $nz2.ToMs -eq -1) ("{0}  [{1}]  refused={2}  after undo {3}..{4}" -f $t1, $w1, $refused, $nz2.FromMs, $nz2.ToMs)
ZCall 'SetLook' @([int]1); ZCall 'RemoveZone' @([int]0)
[void]$zdT.GetMethod('OnOk', $IN).Invoke($zd, @())
$res = $zd.Result
Rep 'zones window: Save returns a copy; the zones it came with are untouched until then' ($res.Count -eq 1 -and $res[0].Look -eq 1 -and $res[0].FromMs -eq 1000 -and $orig.Zones.Count -eq 1 -and $orig.Zones[0].Look -eq 2 -and -not [object]::ReferenceEquals($res[0], (ZP 'Zones')[0]) -and (ZP 'Changed')) ("result {0} (look {1}), original {2} (look {3})" -f $res.Count, $res[0].Look, $orig.Zones.Count, $orig.Zones[0].Look)
$zd.Dispose()
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $ok, $bad)
if ($bad -gt 0) { exit 1 }
