# שדרוג אמיתי מגרסה קודמת לגרסה שפורסמה עכשיו - דרך מנגנון העדכון עצמו.
#
#   powershell -ExecutionPolicy Bypass -File build\test-upgrade-real.ps1 -From v0.7.0
#
# להריץ **אחרי** gh release create, כש-dist\SubtitleStudio.exe הוא בדיוק מה שהועלה.
# מה קורה:
#  1. בונה את התגית -From ב-git worktree תחת D:\Claude\_ss-upgrade (לא בכונן C)
#  2. מריץ אותה כנייד (portable.txt) מתיקייה זמנית
#  3. מחכה להצעת העדכון, מצלם אותה (shots\1-offer.png - להסתכל: התקציר נכון?)
#  4. לוחץ ״לעדכן עכשיו״, עוקב אחרי ההורדה, ובודק שהקובץ שהוחלף זהה ל-dist
#     ושהגרסה החדשה עלתה לבד, לא מציעה עדכון שוב, ונסגרת נקי
#  5. מחזיר את settings.ini של המשתמש ומוחק הכול
#
# **מלכודת:** לחיצה ב-PostMessage לא מגיעה לכפתור של WinForms אם החלון מכוסה -
# ‏WmMouseUp בודק ש-WindowFromPoint מחזיר את הכפתור. לכן החלון מוקפץ ל-TOPMOST
# **בלי הפעלה** (SWP_NOACTIVATE), כדי לא לגנוב את המקלדת מהמשתמש.
# נבדק לראשונה: 0.7.0 -> 0.7.1, 14.9.2026 - הצעה אחרי 2 שניות, הורדה 57 שניות.
param([string]$From = 'v0.7.0')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$base = 'D:\Claude\_ss-upgrade'
$app = Join-Path $base 'app'
$out = Join-Path $base 'shots'
$srcOld = Join-Path $base 'src-old'
New-Item -ItemType Directory $app, $out -Force | Out-Null
if (-not (Test-Path (Join-Path $srcOld 'dist\SubtitleStudio.exe'))) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    git -C $repo worktree add --detach $srcOld $From 2>&1 | Out-Null
    $ErrorActionPreference = $old
    New-Item -ItemType Directory (Join-Path $srcOld 'build\payload') -Force | Out-Null
    Copy-Item (Join-Path $repo 'build\payload\ffmpeg.pack') (Join-Path $srcOld 'build\payload') -Force
    Copy-Item (Join-Path $repo 'build\app.ico') (Join-Path $srcOld 'build') -Force
    cmd /c (Join-Path $srcOld 'build.cmd') | Select-Object -Last 1
}
Copy-Item (Join-Path $srcOld 'dist\SubtitleStudio.exe') (Join-Path $app 'SubtitleStudio.exe') -Force
Set-Content (Join-Path $app 'portable.txt') 'portable' -Encoding ASCII
Remove-Item (Join-Path $app 'SubtitleStudio.exe.old') -ErrorAction SilentlyContinue
$expected = (Get-FileHash (Join-Path $repo 'dist\SubtitleStudio.exe')).Hash
Get-ChildItem (Join-Path $env:TEMP 'SubtitleStudio-*.exe') -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class UW{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
 public static List<string> Texts(IntPtr parent){ var res=new List<string>(); EnumChildWindows(parent, delegate(IntPtr h, IntPtr l){ var sb=new StringBuilder(300); GetWindowTextW(h,sb,300); if(sb.Length>0) res.Add(sb.ToString()); return true;}, IntPtr.Zero); return res; }
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Tops(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p); if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static IntPtr ChildWithText(IntPtr parent, string text){ IntPtr found=IntPtr.Zero;
   EnumChildWindows(parent, delegate(IntPtr h, IntPtr l){ var sb=new StringBuilder(300); GetWindowTextW(h,sb,300); if(sb.ToString()==text){ found=h; return false;} return true;}, IntPtr.Zero); return found; }
}
"@
[void][UW]::SetProcessDPIAware()
function Shot($h, $file) {
    $r = New-Object UW+R; [void][UW]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object Drawing.Bitmap ($r.Rr - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][UW]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($file); $bmp.Dispose()
}
function Log($s) { Write-Host ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $s) }
# כל שורה שמתחילה ב-"!!" היא כישלון. עד 15.9.2026 הבדיקה יצאה בקוד 0 גם אחרי
# "!! no new instance was started" - ירוק על שדרוג שלא קרה.
$script:failures = 0
function Fail($s) { $script:failures++; Log ("!! " + $s) }

$ini = Join-Path $env:APPDATA 'SubtitleStudio\settings.ini'
$iniBak = Join-Path $base 'settings.ini.bak'
if (Test-Path $ini) { Copy-Item $ini $iniBak -Force }
try {
    # עדכון אוטומטי חייב להיות דלוק לבדיקה; ההגדרות המקוריות חוזרות בסוף
    if (Test-Path $ini) {
        $lines = Get-Content $ini -Encoding UTF8 | Where-Object { $_ -notmatch '^autoupdate=' }
        $lines += 'autoupdate=1'
        [IO.File]::WriteAllLines($ini, $lines, (New-Object Text.UTF8Encoding $false))
    }

    $p = Start-Process (Join-Path $app 'SubtitleStudio.exe') -PassThru
    Log ("old version ($From) started pid=" + $p.Id)
    $btn = [IntPtr]::Zero; $dlgH = [IntPtr]::Zero
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 90 -and $btn -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        $p.Refresh(); if ($p.HasExited) { Log "!! the old version exited unexpectedly"; exit 2 }
        foreach ($h in [UW]::Tops([uint32]$p.Id)) {
            $b = [UW]::ChildWithText($h, 'לעדכן עכשיו')
            if ($b -ne [IntPtr]::Zero) { $btn = $b; $dlgH = $h; break }
        }
    }
    if ($btn -eq [IntPtr]::Zero) { Log "NO UPDATE OFFER within 90s"; Stop-Process -Id $p.Id -Force; exit 3 }
    Log ("update offer shown after " + [int]$sw.Elapsed.TotalSeconds + "s")
    Start-Sleep -Milliseconds 800
    Shot $dlgH (Join-Path $out '1-offer.png')

    # WinForms בודק ב-WM_LBUTTONUP ש-WindowFromPoint מחזיר את הכפתור. חלון מכוסה לא
    # יקבל לחיצה. TOPMOST בלי הפעלה - בלי לגנוב את המקלדת מהמשתמש.
    [void][UW]::SetWindowPos($dlgH, [IntPtr](-1), 0, 0, 0, 0, 0x0013)
    Start-Sleep -Milliseconds 300
    $cr = New-Object UW+R; [void][UW]::GetClientRect($btn, [ref]$cr)
    $lp = [IntPtr](([int]($cr.B / 2) -shl 16) -bor [int]($cr.Rr / 2))
    [void][UW]::PostMessage($btn, 0x0201, [IntPtr]1, $lp)   # WM_LBUTTONDOWN
    Start-Sleep -Milliseconds 80
    [void][UW]::PostMessage($btn, 0x0202, [IntPtr]0, $lp)   # WM_LBUTTONUP
    Log "clicked 'update now'"

    # **לא תקרה קבועה.** בשדרוג 0.7.1 ל-0.7.2 (15.9.2026, בלילה) ההורדה זחלה
    # 4MB לדקה, ותקרה של 180 שניות הרגה את התוכנה באמצע - בדיקה ״נכשלת״
    # בגלל מהירות הרשת. מחכים כל עוד הקובץ גדל בדקה האחרונה, עד 20 דקות.
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $lastLog = -10; $shotN = 0
    $lastSize = -1; $lastGrow = 0
    while ($sw.Elapsed.TotalSeconds -lt 1200 -and ($sw.Elapsed.TotalSeconds - $lastGrow) -lt 60) {
        Start-Sleep -Milliseconds 700
        $p.Refresh()
        if ($p.HasExited) { break }
        $tf = Get-ChildItem (Join-Path $env:TEMP 'SubtitleStudio-*.exe') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
        $bytes = if ($tf) { $tf.Length } else { -1 }
        if ($bytes -ne $lastSize) { $lastSize = $bytes; $lastGrow = $sw.Elapsed.TotalSeconds }
        if ($sw.Elapsed.TotalSeconds - $lastLog -ge 6) {
            $lastLog = $sw.Elapsed.TotalSeconds
            $sz = if ($tf) { [int]($tf.Length / 1MB) } else { -1 }
            $desc = @()
            foreach ($h in [UW]::Tops([uint32]$p.Id)) { $r = New-Object UW+R; [void][UW]::GetWindowRect($h, [ref]$r); $desc += (($r.Rr - $r.L).ToString() + 'x' + ($r.B - $r.T) + ':' + (([UW]::Texts($h)) -join '|')) }
            $winLine = ($desc -join ' ;; ')
            Log ("  t=" + [int]$sw.Elapsed.TotalSeconds + " download=" + $sz + "MB" + $(if ($winLine -ne $script:lastWin) { " windows=" + $winLine } else { "" }))
            $script:lastWin = $winLine
            if ($shotN -lt 3) { $tops = [UW]::Tops([uint32]$p.Id); if ($tops.Count -gt 0) { Shot $tops[0] (Join-Path $out ('1b-after-click-' + $shotN + '.png')); $shotN++ } }
        }
    }
    Log ("old version ($From) exited: " + $p.HasExited + " after " + [int]$sw.Elapsed.TotalSeconds + "s")
    if (-not $p.HasExited) { Fail "the old version did not exit - download stalled for a minute, or the swap never started" }

    $new = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $new) {
        Start-Sleep -Milliseconds 500
        $new = Get-CimInstance Win32_Process -Filter "Name='SubtitleStudio.exe'" | Where-Object { $_.ExecutablePath -like "$app*" -and $_.ProcessId -ne $p.Id } | Select-Object -First 1
    }
    $hash = (Get-FileHash (Join-Path $app 'SubtitleStudio.exe')).Hash
    Log ("EXE hash matches dist (the release): " + ($hash -eq $expected))
    if ($hash -ne $expected) { Fail "the swapped EXE is not the release" }
    Log (".old left next to it: " + (Test-Path (Join-Path $app 'SubtitleStudio.exe.old')))
    if (Test-Path (Join-Path $app 'SubtitleStudio.exe.old')) { Fail ".old was not cleaned up" }
    if ($new) {
        Log ("new instance running pid=" + $new.ProcessId)
        Start-Sleep -Seconds 8
        $np = Get-Process -Id $new.ProcessId -ErrorAction SilentlyContinue
        $tops = [UW]::Tops([uint32]$new.ProcessId)
        $main = $null; $best = 0
        foreach ($h in $tops) {
            $r = New-Object UW+R; [void][UW]::GetWindowRect($h, [ref]$r)
            $area = ($r.Rr - $r.L) * ($r.B - $r.T)
            if ($area -gt $best) { $best = $area; $main = $h }
            if ([UW]::ChildWithText($h, 'לעדכן עכשיו') -ne [IntPtr]::Zero) { Fail "the new version offers an update again" }
        }
        if ($main) { Shot $main (Join-Path $out '2-after.png') }
        Log ("windows of new instance: " + $tops.Count)
        [void]$np.CloseMainWindow()
        if (-not $np.WaitForExit(8000)) { Fail "new instance did not close; killing"; Stop-Process -Id $np.Id -Force }
        else { Log "new instance closed cleanly" }
    } else { Fail "no new instance was started" }
}
finally {
    if (Test-Path $iniBak) { Copy-Item $iniBak $ini -Force; Remove-Item $iniBak }
    Get-CimInstance Win32_Process -Filter "Name='SubtitleStudio.exe'" | Where-Object { $_.ExecutablePath -like "$app*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    git -C $repo worktree remove --force $srcOld 2>&1 | Out-Null
    git -C $repo worktree prune 2>&1 | Out-Null
    $ErrorActionPreference = $old
    Write-Host ("shots: " + $out + "  (the folder is removed on the next run)")
    Remove-Item $app, $srcOld -Recurse -Force -ErrorAction SilentlyContinue
}
if ($script:failures -gt 0) { Write-Host ("FAILED: " + $script:failures); exit 1 }
Write-Host "UPGRADE OK"
