# הודעות שגיאה אנושיות (ErrorText) - מול כשלים אמיתיים, לא מול מחרוזות שהומצאו.
#
# **למה ככה:** הכללים ב-ErrorText מתאימים ניסוחים של ffmpeg ושל ווינדוס.
# אם גרסת מנוע עתידית תנסח אחרת, בדיקה מול מחרוזות קבועות תמשיך לעבור,
# והמשתמש יקבל שוב ״Error opening output files״. לכן כל מקרה כאן **מופק**:
# המנוע המוטמע מורץ על קובץ חסר, פגום, נעול וכו', וכתיבת הקבצים באמת נכשלת.
#
# **מה לא מופק:** כונן מלא, וקובץ גדול מדי לדיסק-און-קי. בשביל אלה צריך
# כונן אמיתי מלא, והם נבדקים מול הניסוח המתועד של ffmpeg בלבד.
# צפוי: 35 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class DpiE { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][DpiE]::SetProcessDPIAware()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IF = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }

(T 'Theme').GetField('Scale', $SF).SetValue($null, [float]1.25)
[void](T 'Runtime').GetMethod('Prepare', $SF).Invoke($null, @())
$ffT = T 'Ff'
$exe = [string]$ffT.GetProperty('Exe', $SF).GetValue($null, $null)
$et = T 'ErrorText'
$mFf = $et.GetMethod('Ffmpeg', $SF, $null, [Type[]]@([string]), $null)
$mOf = $et.GetMethod('Of', $SF)

$work = [IO.Path]::GetFullPath((Join-Path $env:TEMP ('ss-err-' + [guid]::NewGuid().ToString('N').Substring(0, 6))))
New-Item -ItemType Directory $work | Out-Null
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
$silent = Join-Path $env:TEMP 'ss-gallery\silent.mp4'
if (-not (Test-Path $silent)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }

# הודעה ״נקייה״: בלי אותיות לטיניות מחוץ לקטעים שעטופים ב-Theme.Ltr
function Clean([string]$s) { return -not ([regex]::Replace($s, "\u202A[^\u202C]*\u202C", '') -match '[A-Za-z]') }

function FfErr([string]$argline) {
    $a = New-Object object[] 5
    $a[0] = $exe; $a[1] = '-hide_banner -nostdin -y ' + $argline; $a[4] = $work
    [void]$ffT.GetMethod('RunSync', $SF).Invoke($null, $a)
    return [string]$a[3]
}
function Human([string]$log) { return [string]$mFf.Invoke($null, @(,$log)) }

# ================= 1. ffmpeg: כשלים אמיתיים =================
Write-Host 'מנוע הווידאו: כשלים אמיתיים'
$rnd = Join-Path $work 'random.mp4'; $b = New-Object byte[] 200000; (New-Object Random 7).NextBytes($b); [IO.File]::WriteAllBytes($rnd, $b)
$all = [IO.File]::ReadAllBytes($media); $half = New-Object byte[] ([int]($all.Length / 2)); [Array]::Copy($all, $half, $half.Length)
$trunc = Join-Path $work 'trunc.mp4'; [IO.File]::WriteAllBytes($trunc, $half)
$txt = Join-Path $work 'notes.avi'; [IO.File]::WriteAllText($txt, 'זה לא סרט, זה קובץ טקסט')
$locked = Join-Path $work 'locked.mp4'; [IO.File]::WriteAllBytes($locked, (New-Object byte[] 10))

$cases = @(
    @{ n = 'קובץ שלא קיים';            a = "-i `"$work\nope.mp4`" -f null -";                         want = 'לא נמצא' },
    @{ n = 'קובץ חתוך באמצע';          a = "-i `"$trunc`" -f null -";                                 want = 'לא שלם' },
    @{ n = 'בייטים אקראיים';           a = "-i `"$rnd`" -f null -";                                   want = 'פגום' },
    @{ n = 'קובץ טקסט בשם של סרט';     a = "-i `"$txt`" -f null -";                                   want = 'פגום' },
    @{ n = 'אין פס קול';               a = "-i `"$silent`" -map 0:a -c copy `"$work\a.m4a`"";          want = 'אין בו פס קול' },
    @{ n = 'אין ערוץ כתוביות';         a = "-i `"$media`" -map 0:s:0 `"$work\s.srt`"";                want = 'אין בו כתוביות' },
    @{ n = 'תיקיית יעד שלא קיימת';     a = "-i `"$media`" -t 1 `"$work\nodir\x.mp4`"";                want = 'התיקייה' },
    @{ n = 'תיקייה מוגנת';             a = "-i `"$media`" -t 1 `"$env:WINDIR\ss-err-test.mp4`"";      want = 'אי אפשר לשמור שם' },
    @{ n = 'מסנן שלא קיים במנוע';      a = "-i `"$media`" -vf nosuchfilter -f null -";                 want = 'חסר רכיב' },
    @{ n = 'מקודד שלא קיים במנוע';     a = "-i `"$media`" -c:v libnosuch -f null -";                   want = 'חסר רכיב' },
    @{ n = 'וידאו לתוך WAV';           a = "-i `"$media`" -map 0:v -c:v copy `"$work\v.wav`"";         want = 'לא מתאים' },
    @{ n = 'צריבה עם קובץ כתוביות חסר'; a = "-i `"$media`" -vf subtitles=nope.ass -t 1 -f null -";     want = 'הכתוביות הזמני' }
)
$msgs = @()
foreach ($c in $cases) {
    $log = FfErr $c.a
    $h = Human $log
    $msgs += $h
    Check $c.n ($h.Contains($c.want)) $h
}
# נעול: קובץ יעד שפתוח בתוכנה אחרת בלי שיתוף
$fs = [IO.File]::Open($locked, 'Open', 'ReadWrite', 'None')
try { $log = FfErr "-i `"$media`" -t 1 `"$locked`"" } finally { $fs.Close() }
$h = Human $log; $msgs += $h
Check 'קובץ יעד שפתוח בנגן' ($h.Contains('פתוח בתוכנה אחרת')) $h

# ניסוחים שאי אפשר להפיק בלי כונן אמיתי מלא
$h = Human "[out#0/mp4 @ 0000] Error writing trailer: No space left on device`nConversion failed!"; $msgs += $h
Check 'כונן מלא (ניסוח מתועד)' ($h.Contains('מקום בכונן')) $h
$h = Human "av_interleaved_write_frame(): File too large`nConversion failed!"; $msgs += $h
Check 'קובץ גדול מ-4GB לדיסק-און-קי (ניסוח מתועד)' ($h.Contains('4GB')) $h

$h = Human ("frame=  100 fps=50`n[h264 @ 1] Invalid data found when processing input`nframe=  200 fps=50`n" + ((1..40 | ForEach-Object { "frame=$_" }) -join "`n") + "`nError writing trailer: No space left on device")
Check 'אזהרה ישנה באמצע היומן לא גוברת על הסיבה בסוף' ($h.Contains('מקום בכונן')) $h
$h = Human "something nobody has seen before`nConversion failed!"
Check 'כשל לא מוכר: הודעה כללית שמפנה ליומן' ($h.Contains('ביומן')) $h
$allClean = $true; foreach ($m in $msgs) { if (-not (Clean $m)) { $allClean = $false; Write-Host "     לא נקי: $m" } }
Check 'אף הודעה לא מכילה אנגלית מחוץ ל-Ltr' $allClean ''

# ================= 2. ווינדוס: חריגות אמיתיות =================
Write-Host 'קבצים: חריגות אמיתיות'
function Throws([scriptblock]$sb) { try { & $sb; return $null } catch { $e = $_.Exception; while ($e -is [Management.Automation.MethodInvocationException] -and $e.InnerException) { $e = $e.InnerException }; return $e } }
function Of($ex) { $o = [object[]]@($ex.psobject.BaseObject); return [string]$mOf.Invoke($null, $o) }

$f = Join-Path $work 'open.srt'; [IO.File]::WriteAllText($f, 'x')
$fs = [IO.File]::Open($f, 'Open', 'ReadWrite', 'None')
$ex = Throws { [IO.File]::WriteAllText($f, 'y') }
$fs.Close()
$h = Of $ex
Check 'קובץ פתוח בתוכנה אחרת' ($h.Contains('פתוח בתוכנה אחרת')) ("$h  [" + $ex.GetType().Name + " 0x" + ('{0:X}' -f $ex.HResult) + "]")

$ro = Join-Path $work 'readonly.srt'; [IO.File]::WriteAllText($ro, 'x'); (Get-Item $ro).IsReadOnly = $true
$ex = Throws { [IO.File]::WriteAllText($ro, 'y') }
(Get-Item $ro).IsReadOnly = $false
$h = Of $ex
Check 'קובץ לקריאה בלבד' ($h.Contains('אין הרשאה')) ("$h  [" + $ex.GetType().Name + "]")

$ex = Throws { [IO.File]::WriteAllText((Join-Path $env:WINDIR 'ss-err-test.srt'), 'y') }
$h = Of $ex
Check 'תיקייה מוגנת' ($h.Contains('אין הרשאה')) ("$h  [" + $ex.GetType().Name + "]")

$ex = Throws { [IO.File]::WriteAllText((Join-Path $work 'no\such\dir\x.srt'), 'y') }
$h = Of $ex
Check 'תיקייה שלא קיימת' ($h.Contains('התיקייה לא קיימת')) ("$h  [" + $ex.GetType().Name + "]")

$ex = Throws { [void][IO.File]::ReadAllBytes((Join-Path $work 'nope.srt')) }
$h = Of $ex
Check 'קובץ שלא קיים' ($h.Contains('לא נמצא')) ("$h  [" + $ex.GetType().Name + "]")

$ex = Throws { [void][IO.File]::ReadAllBytes('Q:\nope\x.srt') }
$h = Of $ex
Check 'כונן שלא קיים' ($h.Contains('לא נמצא') -or $h.Contains('לא קיימת') -or $h.Contains('נותק')) ("$h  [" + $ex.GetType().Name + "]")

$ex = Throws { [void][Diagnostics.Process]::Start((Join-Path $work 'nope.exe')) }
$h = Of $ex
Check 'הפעלה של קובץ שלא קיים' ($h.Contains('לא נמצא')) ("$h  [" + $ex.GetType().Name + "]")
Check 'לחיצה על ״לא״ בחלון ההרשאות' ((Of (New-Object ComponentModel.Win32Exception 1223)).Contains('בוטלה')) ''
Check 'דיסק מלא (קוד 112)' ((Of (New-Object IO.IOException 'x', ([int]0x80070070))).Contains('מקום בכונן')) ''
Check 'הודעה שכבר בעברית עוברת כמו שהיא' ((Of (New-Object IO.InvalidDataException 'הקובץ נשמר בגרסה חדשה')) -eq 'הקובץ נשמר בגרסה חדשה') ''
$h = Of (New-Object InvalidOperationException 'Collection was modified')
Check 'חריגה לא מוכרת: עברית קודם, והפרטים עטופים' ($h.StartsWith('הפעולה לא הצליחה') -and (Clean $h)) $h

# ================= 3. חלון ההתקדמות =================
Write-Host 'חלון ההתקדמות'
$jobT = T 'FfJob'
$pdT = T 'ProgressDlg'
function RunDlg([string]$args2, [bool]$cancel) {
    $job = [Activator]::CreateInstance($jobT)
    $job.Args = $args2
    $job.WorkDir = $work
    $job.TotalMs = 40000
    $dlg = [Activator]::CreateInstance($pdT, $IF -bor [Reflection.BindingFlags]::CreateInstance, $null, @('בדיקה', $job), $null)
    [void]$ffT.GetMethod('RunJob', $SF).Invoke($null, @(,$job))
    if ($cancel) { Start-Sleep -Milliseconds 600; $job.Cancel() }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $job.Done -and $sw.ElapsedMilliseconds -lt 30000) { Start-Sleep -Milliseconds 50 }
    Start-Sleep -Milliseconds 200
    $head = [string]$pdT.GetProperty('Headline', $IF).GetValue($dlg, $null)
    return @{ Dlg = $dlg; Head = $head; Job = $job }
}
$fs = [IO.File]::Open($locked, 'Open', 'ReadWrite', 'None')
try { $r = RunDlg "-i `"$media`" -t 2 `"$locked`"" $false } finally { $fs.Close() }
Check 'קובץ נעול: ההודעה בחלון אנושית' ($r.Head.Contains('פתוח בתוכנה אחרת')) $r.Head
$logTxt = $r.Job.Log.ToString()
Check 'והיומן הגולמי עדיין שם, מאחורי ״יומן״' ($logTxt -match 'Permission denied') ''

# **מודדים את הציור, לא את הטקסט.** גרסה ראשונה של הבדיקה מדדה את ההודעה
# עם גלישת שורות ואמרה ״נכנס״ - והחלון צייר אותה בשורה אחת עם ״...״.
# כאן מציירים את החלון לתמונה, ומודדים כמה גבוה הדיו האדום.
$dlg = $r.Dlg
[void]$dlg.Handle
$bmp = New-Object Drawing.Bitmap $dlg.Width, $dlg.Height
$dlg.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $dlg.Width, $dlg.Height))
$bad = (T 'Theme').GetProperty('Bad', $SF).GetValue($null, $null)
$sc = [double](T 'Theme').GetField('Scale', $SF).GetValue($null)
$top = -1; $bot = -1
for ($y = [int](50 * $sc); $y -lt [int](100 * $sc); $y++) {
    for ($x = [int](22 * $sc); $x -lt [int](498 * $sc); $x += 2) {
        $px = $bmp.GetPixel($x, $y)
        if ([Math]::Abs($px.R - $bad.R) + [Math]::Abs($px.G - $bad.G) + [Math]::Abs($px.B - $bad.B) -lt 90) { if ($top -lt 0) { $top = $y }; $bot = $y; break }
    }
}
$bmp.Dispose()
$lineH = (T 'Theme').GetProperty('Small', $SF).GetValue($null, $null).Height
Check 'ההודעה הארוכה מצוירת בשתי שורות, לא נחתכת' ($top -ge 0 -and ($bot - $top) -gt 1.5 * $lineH) ("דיו " + ($bot - $top) + "px, שורה $lineH px")
$r.Dlg.Dispose()

$slow = "-re -i `"$media`" -f null -"
$r = RunDlg $slow $true
Check 'ביטול: ״הפעולה בוטלה״, לא ״לא הצליח: בוטל״' ($r.Head -eq 'הפעולה בוטלה') $r.Head
$r.Dlg.Dispose()

$r = RunDlg "-i `"$media`" -t 1 -f null -" $false
Check 'הצלחה' ($r.Head -eq 'הפעולה הושלמה בהצלחה') $r.Head
$r.Dlg.Dispose()

# כל הודעה נכנסת לשטח שלה בחלון: 476 על 44 לוגיים, באותם דגלים ש-Theme.Str מצייר
$scale = [double](T 'Theme').GetField('Scale', $SF).GetValue($null)
$font = (T 'Theme').GetProperty('Small', $SF).GetValue($null, $null)
$w = [int][Math]::Round(476 * $scale); $hmax = [int][Math]::Round(44 * $scale)
$worst = 0; $worstMsg = ''
foreach ($m in $msgs) {
    $sz = [Windows.Forms.TextRenderer]::MeasureText($m, $font, (New-Object Drawing.Size $w, 10000), [Windows.Forms.TextFormatFlags]'NoPadding,NoPrefix,RightToLeft,Right,WordBreak')
    if ($sz.Height -gt $worst) { $worst = $sz.Height; $worstMsg = $m }
}
Check 'כל ההודעות נכנסות לחלון ההתקדמות' ($worst -le $hmax) ("הגבוהה: $worst מתוך $hmax  ($worstMsg)")

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
