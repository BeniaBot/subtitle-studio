# הרצה אמיתית של התוכנה עם קובץ פרויקט כארגומנט - כמו לחיצה כפולה.
#
# כל שאר הבדיקות טוענות את ה-EXE כאסמבלי ומפעילות מתודות. זו היחידה שעוברת
# במסלול האמיתי: Main -> OpenOnStart -> Shown -> OpenAny -> OpenProject, בלי
# SUBSTUDIO_TEST (שמדלג בדיוק על החלק הזה). מה נבדק:
#  - הנתיב המוחלט לסרט שבור בכוונה; היחסי אמור למצוא אותו
#  - התוכנה לא קורסת, והחלון מצולם (%TEMP%\ss-smoke\smoke-open.png - להסתכל!)
#  - סגירה מיד אחרי פתיחה לא שואלת ״לשמור?״ (באג ותיק של RaiseChanged)
# ההגדרות האמיתיות של המשתמש מגובות ומוחזרות, כי פתיחה מוסיפה לאחרונים.
# -Light: פותח בערכה הבהירה. -Lang en: בממשק אנגלי. שניהם דרך קובץ ההגדרות,
# שמגובה לפני ומוחזר אחרי - ההעדפות של המשתמש לא משתנות.
param([switch]$Light, [string]$Lang = '')
$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\subtitle-studio'
$sp = Join-Path $env:TEMP 'ss-smoke'
New-Item -ItemType Directory $sp -Force | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class SW{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p); if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static string Title(IntPtr h){ var sb=new StringBuilder(300); GetWindowTextW(h,sb,300); return sb.ToString(); }
}
"@
[void][SW]::SetProcessDPIAware()

function Shot($h, $file) {
    $r = New-Object SW+R; [void][SW]::GetWindowRect($h, [ref]$r)
    $w = $r.Rr - $r.L; $hh = $r.B - $r.T
    $bmp = New-Object Drawing.Bitmap $w, $hh
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][SW]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($file); $bmp.Dispose()
    return "$w x $hh"
}

# ---- ההגדרות האמיתיות של המשתמש: גיבוי, ושחזור בסוף ----
$ini = Join-Path $env:APPDATA 'SubtitleStudio\settings.ini'
$iniBak = Join-Path $sp 'settings.ini.bak'
if (Test-Path $ini) { Copy-Item $ini $iniBak -Force }
if ($Light -or $Lang -ne '') {
    $lines = @(); if (Test-Path $ini) { $lines = @([IO.File]::ReadAllLines($ini, [Text.Encoding]::UTF8) | Where-Object { -not ($_ -like 'dark=*') -and -not ($_ -like 'lang=*') }) }
    $lines += ('dark=' + $(if ($Light) { '0' } else { '1' }))
    if ($Lang -ne '') { $lines += ('lang=' + $Lang) }
    New-Item -ItemType Directory (Split-Path $ini) -Force | Out-Null
    [IO.File]::WriteAllLines($ini, $lines, (New-Object Text.UTF8Encoding $false))
}

try {
    # ---- פרויקט ----
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
    $ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
    $pdT = $asm.GetType('SubtitleStudio.ProjectData'); $cueT = $asm.GetType('SubtitleStudio.Cue')
    $ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
    $dir = Join-Path $sp 'smoke'; New-Item -ItemType Directory $dir -Force | Out-Null
    $media = Join-Path $dir 'שיעור בדיקה.mp4'
    Copy-Item (Join-Path $env:TEMP 'ss-gallery\test.mp4') $media -Force
    $d = [Activator]::CreateInstance($pdT)
    $d.MediaPath = 'Z:\נתיב\שכבר\לא\קיים.mp4'          # המוחלט שבור בכוונה
    $d.MediaRelative = 'שיעור בדיקה.mp4'                  # היחסי אמור להציל
    $i = 0
    foreach ($t in @('ברוכים הבאים לשיעור', 'היום נלמד על כתוביות', 'והשורה הזאת עוד לא תוזמנה')) {
        $c = $ctor.Invoke(@([long](800 + $i * 1400), [long](2000 + $i * 1400), [string]$t))
        if ($i -eq 2) { $c.Untimed = $true }
        $d.Cues.Add($c); $i++
    }
    $d.Position = 2100; $d.InPoint = 700; $d.OutPoint = 3300
    $json = $asm.GetType('SubtitleStudio.Project').GetMethod('ToJson', $ST).Invoke($null, @(,$d))
    $prjFile = Join-Path $dir 'שיעור בדיקה.subtext'
    [IO.File]::WriteAllText($prjFile, $json, (New-Object Text.UTF8Encoding $false))

    Remove-Item "$env:TEMP\SubStudio-error.txt" -ErrorAction SilentlyContinue
    $p = Start-Process (Join-Path $root 'dist\Subtext.exe') -ArgumentList ('"' + $prjFile + '"') -PassThru
    Start-Sleep -Seconds 9
    $p.Refresh()
    if ($p.HasExited) {
        Write-Host "CRASHED exit=$($p.ExitCode)"
        if (Test-Path "$env:TEMP\SubStudio-error.txt") { Get-Content "$env:TEMP\SubStudio-error.txt" -TotalCount 25 }
        exit 2
    }
    $wins = [SW]::Wins([uint32]$p.Id)
    $main = $null; $best = 0
    foreach ($h in $wins) {
        $r = New-Object SW+R; [void][SW]::GetWindowRect($h, [ref]$r)
        $area = ($r.Rr - $r.L) * ($r.B - $r.T)
        Write-Host ("  window: " + [SW]::Title($h) + "  " + ($r.Rr - $r.L) + "x" + ($r.B - $r.T))
        if ($area -gt $best) { $best = $area; $main = $h }
    }
    Write-Host ("shot: " + (Shot $main (Join-Path $sp 'smoke-open.png')))

    # ---- סגירה: פרויקט שרק נפתח לא אמור לשאול ״לשמור?״ ----
    [void]$p.CloseMainWindow()
    if ($p.WaitForExit(6000)) { Write-Host "CLOSED CLEANLY (no save prompt)" }
    else {
        Write-Host "DID NOT CLOSE - a dialog is probably open:"
        foreach ($h in [SW]::Wins([uint32]$p.Id)) { Write-Host ("  " + [SW]::Title($h)) }
        $p2 = [SW]::Wins([uint32]$p.Id) | Select-Object -First 1
        if ($p2) { Shot $p2 (Join-Path $sp 'smoke-close.png') | Out-Null }
        Stop-Process -Id $p.Id -Force
    }
}
finally {
    if (Test-Path $iniBak) { Copy-Item $iniBak $ini -Force; Remove-Item $iniBak }
}
