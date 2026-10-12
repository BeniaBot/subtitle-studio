# **עדכון של העותק המותקן אצל המשתמש** - דרך גיטהאב האמיתי, כמו שהוא יקבל אותו (12.10.2026, באישור בנימין).
#
#   powershell -ExecutionPolicy Bypass -File build\test-upgrade-installed.ps1 [-Exe <נתיב>]
#
# בשונה מ-test-upgrade-real (בונה גרסה ישנה ומריץ עותק נייד שנמחק בסוף): כאן **הקובץ המותקן עצמו**. לא מוחקים כלום
# מתיקיית ההתקנה, ולא סוגרים שום תהליך שלא הבדיקה פתחה. להריץ אחרי שחרור, כש-dist\Subtext.exe הוא מה שהועלה.
# **רק באישור המשתמש** - זו ההתקנה שלו, והחלון של ההצעה מופיע על המסך שלו.
#
# שימו לב: מתוך Claude, הרישום (HKCU) והגדרות ‏%APPDATA% הם עותק פרטי - ״אפליקציות מותקנות״ האמיתי מתעדכן כשהמשתמש
# עצמו פותח את התוכנה בפעם הבאה (Install.Refresh). הקובץ עצמו (‏%LOCALAPPDATA%\Programs) - אמיתי.
param([string]$Exe = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out = 'D:\Claude\_ss-upgrade-installed'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $out -Force | Out-Null
if (-not $Exe) {
    foreach ($d in @('Programs\SubtitleStudio', 'Programs\Subtext')) {
        $c = Join-Path $env:LOCALAPPDATA (Join-Path $d 'Subtext.exe')
        if (Test-Path $c) { $Exe = $c; break }
    }
}
if (-not $Exe -or -not (Test-Path $Exe)) { Write-Host '!! no installed copy found'; exit 2 }
$dir = Split-Path $Exe -Parent
$expected = (Get-FileHash (Join-Path $repo 'dist\Subtext.exe')).Hash

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class UI2{
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
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Tops(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p); if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static IntPtr ChildWithText(IntPtr parent, string text){ IntPtr found=IntPtr.Zero;
   EnumChildWindows(parent, delegate(IntPtr h, IntPtr l){ var sb=new StringBuilder(300); GetWindowTextW(h,sb,300); if(sb.ToString()==text){ found=h; return false;} return true;}, IntPtr.Zero); return found; }
}
"@
[void][UI2]::SetProcessDPIAware()
function Shot($h, $file) {
    $r = New-Object UI2+R; [void][UI2]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object Drawing.Bitmap ($r.Rr - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][UI2]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($file); $bmp.Dispose()
}
function Log($s) { Write-Host ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $s) }
$script:failures = 0
function Fail($s) { $script:failures++; Log ("!! " + $s) }

Log ("installed: " + $Exe + "  version " + (Get-Item $Exe).VersionInfo.FileVersion)
$before = @(Get-ChildItem $dir | ForEach-Object { $_.Name })
Log ("folder: " + ($before -join ', '))
$running = @(Get-Process Subtext -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($running.Count -gt 0) { Log '!! the installed copy is running - not touching it. Close it and run again'; exit 3 }

$ini = Join-Path $env:APPDATA 'SubtitleStudio\settings.ini'
$iniBak = Join-Path $out 'settings.ini.bak'
if (Test-Path $ini) { Copy-Item $ini $iniBak -Force }
$p = $null; $newId = 0
try {
    if (Test-Path $ini) {
        $lines = Get-Content $ini -Encoding UTF8 | Where-Object { $_ -notmatch '^autoupdate=' }
        $lines += 'autoupdate=1'
        [IO.File]::WriteAllLines($ini, $lines, (New-Object Text.UTF8Encoding $false))
    }
    $p = Start-Process $Exe -PassThru
    Log ("started the installed copy, pid=" + $p.Id)
    $btn = [IntPtr]::Zero; $dlgH = [IntPtr]::Zero
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 90 -and $btn -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        $p.Refresh(); if ($p.HasExited) { Fail "it exited before offering an update"; break }
        foreach ($h in [UI2]::Tops([uint32]$p.Id)) {
            $b = [UI2]::ChildWithText($h, 'לעדכן עכשיו')
            if ($b -ne [IntPtr]::Zero) { $btn = $b; $dlgH = $h; break }
        }
    }
    if ($btn -eq [IntPtr]::Zero) { Fail "no update offer within 90 seconds" }
    else {
        Log ("update offer after " + [int]$sw.Elapsed.TotalSeconds + "s")
        Start-Sleep -Milliseconds 800
        Shot $dlgH (Join-Path $out '1-offer.png')
        [void][UI2]::SetWindowPos($dlgH, [IntPtr](-1), 0, 0, 0, 0, 0x0013)
        Start-Sleep -Milliseconds 300
        $cr = New-Object UI2+R; [void][UI2]::GetClientRect($btn, [ref]$cr)
        $lp = [IntPtr](([int]($cr.B / 2) -shl 16) -bor [int]($cr.Rr / 2))
        [void][UI2]::PostMessage($btn, 0x0201, [IntPtr]1, $lp); Start-Sleep -Milliseconds 80
        [void][UI2]::PostMessage($btn, 0x0202, [IntPtr]0, $lp)
        Log "clicked 'update now'"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt 600) { Start-Sleep -Milliseconds 700; $p.Refresh(); if ($p.HasExited) { break } }
        Log ("the old version exited: " + $p.HasExited + " after " + [int]$sw.Elapsed.TotalSeconds + "s")
        if (-not $p.HasExited) { Fail "the old version did not exit" }
        $sw = [Diagnostics.Stopwatch]::StartNew(); $new = $null
        while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $new) {
            Start-Sleep -Milliseconds 500
            $new = Get-CimInstance Win32_Process -Filter "Name='Subtext.exe'" | Where-Object { $_.ExecutablePath -eq $Exe -and $_.ProcessId -ne $p.Id } | Select-Object -First 1
        }
        $hash = (Get-FileHash $Exe).Hash
        Log ("installed file now: version " + (Get-Item $Exe).VersionInfo.FileVersion + ", identical to the release: " + ($hash -eq $expected))
        if ($hash -ne $expected) { Fail "the installed file is not the release" }
        if ($new) {
            $newId = $new.ProcessId
            Log ("new version running pid=" + $newId)
            Start-Sleep -Seconds 8
            Log (".old left: " + (Test-Path ($Exe + '.old')))
            if (Test-Path ($Exe + '.old')) { Fail ".old was not cleaned up" }
            $tops = [UI2]::Tops([uint32]$newId); $main = $null; $best = 0
            foreach ($h in $tops) {
                $r = New-Object UI2+R; [void][UI2]::GetWindowRect($h, [ref]$r); $area = ($r.Rr - $r.L) * ($r.B - $r.T)
                if ($area -gt $best) { $best = $area; $main = $h }
                if ([UI2]::ChildWithText($h, 'לעדכן עכשיו') -ne [IntPtr]::Zero) { Fail "the new version offers an update again" }
            }
            if ($main) { Shot $main (Join-Path $out '2-after.png') }
            $np = Get-Process -Id $newId -ErrorAction SilentlyContinue
            if ($np) { [void]$np.CloseMainWindow(); if (-not $np.WaitForExit(10000)) { Fail "the new version did not close by itself (left open for the user)" } else { Log "new version closed cleanly" } }
        } else { Fail "no new version was started" }
        $after = @(Get-ChildItem $dir | ForEach-Object { $_.Name })
        Log ("folder after: " + ($after -join ', '))
        foreach ($n in $before) { if ($after -notcontains $n) { Fail ("missing after the update: " + $n) } }
    }
}
finally {
    if (Test-Path $iniBak) { Copy-Item $iniBak $ini -Force; Remove-Item $iniBak }
    # רק מה שהבדיקה פתחה - ושעדיין פתוח (ההצעה לא הופיעה, או נתקע)
    if ($p -and -not $p.HasExited) { try { [void]$p.CloseMainWindow(); [void]$p.WaitForExit(8000) } catch { } }
}
Write-Host ("shots: " + $out)
if ($script:failures -gt 0) { Write-Host ("FAILED: " + $script:failures); exit 1 }
Write-Host "INSTALLED UPGRADE OK"
