# readme-shots.ps1 - צילומי המסך של README, מחומר שכולו שלנו: תמונה של מעברי צבע רגועים, ו״דיבור״ סינתטי
# (הברות ושתיקות בין משפטים - גל שנראה כמו דיבור, בלי קול של אף אחד). הכתוביות מתוזמנות למשפטים.
# מסך הפתיחה עם רשימת אחרונים נקייה (בלי קבצי בדיקה), מסך העבודה, וחלון החיתוך. מחוץ למסך, כמו main-shot.
#   readme-shots.ps1 [-Lang en] [-Light] [-Out dir]
# הפלט: <Out>\start-<ערכה>-<שפה>.png, work-..., cut-...   (ברירת מחדל: %TEMP%\ss-readme)
param([string]$Lang = 'he', [switch]$Light, [string]$Out = '', [int]$W = 1493, [int]$H = 997)
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Runtime.InteropServices;
public static class ReadmeDpi {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    public struct RECT { public int L, T, R, B; }
    // החלון כמו שווינדוס מציגה אותו, בלי השוליים השקופים (לגרירת שינוי גודל) שמסביבו
    public static Bitmap Grab(IntPtr h) {
        RECT w; GetWindowRect(h, out w);
        RECT v; if (DwmGetWindowAttribute(h, 9, out v, Marshal.SizeOf(typeof(RECT))) != 0) v = w;
        Bitmap full = new Bitmap(w.R - w.L, w.B - w.T);
        using (Graphics g = Graphics.FromImage(full)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
        Rectangle crop = new Rectangle(v.L - w.L, v.T - w.T, v.R - v.L, v.B - v.T);
        Bitmap b = full.Clone(crop, full.PixelFormat);
        full.Dispose();
        return b;
    }
}
'@
[void][ReadmeDpi]::SetProcessDPIAware()
[System.Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
function Pump([int]$ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }

(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]1.25)
(TY 'Theme').GetField('Dark', $ST).SetValue($null, (-not $Light))
[void](TY 'Lang').GetMethod('Set', $ST).Invoke($null, @([string]$Lang))
if (-not (TY 'Fonts').GetProperty('Ready', $ST).GetValue($null, $null)) { throw 'the embedded font did not load' }
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$ffx = [string](TY 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
if ($Out -eq '') { $Out = Join-Path $env:TEMP 'ss-readme' }
New-Item -ItemType Directory -Force $Out | Out-Null
$tag = $(if ($Light) { 'light' } else { 'dark' }) + '-' + $Lang

# ---- החומר: 40 שניות. משפט = כ-3 שניות של הברות, ושתיקה של שנייה ורבע ----
$dur = 40
$demo = Join-Path $Out 'demo3.mp4'
$on = 'gt(sin(2*PI*0.23*t-1.6),-0.6)'
$envl = "pow(max(0,sin(2*PI*3.1*t)),1.6)*$on*(0.55+0.45*sin(2*PI*0.37*t))"
$voice = "($envl)*(0.45*sin(2*PI*128*t)+0.25*sin(2*PI*256*t+1)+0.12*sin(2*PI*390*t)+0.18*(random(0)*2-1))"
if (-not (Test-Path $demo)) {
    & $ffx -nostdin -hide_banner -loglevel error -y `
        -f lavfi -i "gradients=s=1280x720:c0=0x1b2a41:c1=0x324a5f:c2=0x0c7c84:c3=0x23395b:nb_colors=4:speed=0.004:duration=${dur}:rate=25" `
        -f lavfi -i "aevalsrc='$voice':s=44100:d=$dur" `
        -filter_complex '[0:v]vignette=PI/5[v]' -map '[v]' -map 1:a -c:v libx264 -g 25 -keyint_min 25 -sc_threshold 0 -crf 20 -pix_fmt yuv420p -c:a aac -b:a 128k -shortest $demo
}
# המשפטים: מתי התנאי $on מתקיים (אותה נוסחה, בצעדים של מאית)
$spans = @(); $s0 = -1
for ($i = 0; $i -le $dur * 100; $i++) {
    $t = $i / 100.0
    $isOn = [Math]::Sin(2 * [Math]::PI * 0.23 * $t - 1.6) -gt -0.6
    if ($isOn -and $s0 -lt 0) { $s0 = $t }
    if ((-not $isOn -or $i -eq $dur * 100) -and $s0 -ge 0) { $spans += , @($s0, $t); $s0 = -1 }
}
$he = @('ברוכים הבאים לשיעור', 'היום נלמד להוסיף כתוביות', 'עוצרים איפה שהדיבור מתחיל', 'לוחצים על הכפתור הכחול',
        'והכתובית מופיעה על התמונה', 'אפשר גם לתמלל ולתרגם', 'ובסוף - יוצרים סרט חדש', 'הסרט המקורי לא משתנה', 'בהצלחה!', 'נתראה בשיעור הבא')
$en = @('Welcome to the lesson', 'Today: adding subtitles', 'Pause where the speech begins', 'Press the blue button',
        'The subtitle shows on the picture', 'You can transcribe and translate', 'Then make a new video', 'The original never changes', 'Good luck!', 'See you next time')
$texts = if ($Lang -eq 'en') { $en } else { $he }

function NewMain {
    $m = [Activator]::CreateInstance((TY 'MainForm'))
    $m.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $m.Location = New-Object Drawing.Point -3000, -3000
    $m.ShowInTaskbar = $false
    $m.Show()
    $m.ClientSize = New-Object Drawing.Size $W, $H
    Pump 400
    return $m
}
# החלון הראשי - צילום אמיתי (שורת הכותרת של ווינדוס); חלון בלי מסגרת - DrawToBitmap (שם PrintWindow השאיר
# כפתורים שעוד לא צוירו שחורים)
function Snap($f, $file) {
    if ($f.FormBorderStyle -eq 'None') {
        $bmp = New-Object Drawing.Bitmap $f.Width, $f.Height
        $f.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $f.Width, $f.Height))
    } else { $bmp = [ReadmeDpi]::Grab($f.Handle) }
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host ('  ' + $file)
}

# ---- מסך הפתיחה: אחרונים נקיים ----
$recent = (TY 'Settings').GetField('Recent', $ST)
$names = if ($Lang -eq 'en') { @('Lesson 1.mp4', 'Family event.mp4', 'Lecture.subtext') } else { @('שיעור א.mp4', 'בר מצווה.mp4', 'הרצאה.subtext') }
$rdir = Join-Path $Out ('recent-' + $Lang + '\' + $(if ($Lang -eq 'en') { 'Videos' } else { 'סרטים' })); New-Item -ItemType Directory -Force $rdir | Out-Null
$list = New-Object 'System.Collections.Generic.List[string]'
foreach ($n in $names) { $f = Join-Path $rdir $n; if (-not (Test-Path $f)) { Copy-Item $demo $f }; $list.Add($f) }
$recent.SetValue($null, $list)
$m = NewMain
Snap $m (Join-Path $Out "start-$tag.png")
$m.Close(); $m.Dispose()

# ---- מסך העבודה ----
$m = NewMain
[void](TY 'MainForm').GetMethod('OpenMedia', $IN).Invoke($m, @([string]$demo))
$wave = (TY 'MainForm').GetField('_wave', $IN).GetValue($m)
$sw = [Diagnostics.Stopwatch]::StartNew(); while (-not $wave.Ready -and $sw.Elapsed.TotalSeconds -lt 60) { Pump 100 }
$doc = (TY 'MainForm').GetField('_doc', $IN).GetValue($m)
$cues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
for ($i = 0; $i -lt [Math]::Min($spans.Count, $texts.Count); $i++) {
    if ($spans[$i][1] -gt $dur - 1) { break }
    $a = [long]([Math]::Round($spans[$i][0], 1) * 1000); $b = [long]([Math]::Round($spans[$i][1], 1) * 1000 + 300)
    $cues.Add((NewOf 'Cue' @([int64]$a, [int64]$b, [string]$texts[$i])))
}
$pick = 4
(TY 'Cue').GetField('Selected', $IN).SetValue($cues[$pick], $true)
foreach ($n in 'RefreshAll', 'LoadEditor', 'UpdateHint') { $mm = (TY 'MainForm').GetMethod($n, $IN); if ($mm) { [void]$mm.Invoke($m, @()) } }
[void](TY 'MainForm').GetMethod('SeekPlayer', $IN).Invoke($m, @([long](($cues[$pick].Start + $cues[$pick].End) / 2)))
Pump 1500
Snap $m (Join-Path $Out "work-$tag.png")

# ---- חלון החיתוך: שני קטעים לשמירה והסרה בתוך הראשון ----
$mi = (TY 'MainForm').GetField('_mi', $IN).GetValue($m)
$D = [long]$mi.DurationMs
$secT = TY 'CutSection'
$secs = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($secT))
$dlg = [Activator]::CreateInstance((TY 'CutDlg'), @($m, $mi, $doc, $wave, $secs))
$dlg.StartPosition = 'Manual'; $dlg.Location = New-Object Drawing.Point -4000, -3000
$dlg.Show(); Pump 300
$add = (TY 'CutDlg').GetMethod('AddSection', $IN)
$secs.Clear()
# תחילות בשניות שלמות - שם פריימי המפתח של החומר (אחד בשנייה), אז חיתוך מהיר מדויק ואין הערה
[void]$add.Invoke($dlg, @([long]([Math]::Floor($spans[1][0]) * 1000), [long]($spans[3][1] * 1000), $true))
[void]$add.Invoke($dlg, @([long]($spans[2][0] * 1000 - 200), [long]([Math]::Ceiling($spans[2][1] + 0.2) * 1000), $false))
[void]$add.Invoke($dlg, @([long]([Math]::Floor($spans[6][0]) * 1000), [long]($spans[7][1] * 1000), $true))
(TY 'CutDlg').GetField('_view', $IN).GetValue($dlg).FitAll()
[void](TY 'MainForm').GetMethod('SeekPlayer', $IN).Invoke($m, @([long]($spans[1][0] * 1000 + 1500)))
Pump 1500
Snap $dlg (Join-Path $Out "cut-$tag.png")
$dlg.Close(); $m.Close(); $m.Dispose()
