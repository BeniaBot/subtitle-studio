# שולח מקש לחלון הרץ ומצלם (כלי פיתוח)
param([string]$Keys = "", [int]$Wait = 2, [string]$Shot = "poke.png", [int]$WinIndex = -1)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public class PM{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
}
"@
[void][PM]::SetProcessDPIAware()
$dest = "$env:TEMP\ss-gallery"
$p = Get-Process SubtitleStudio -ErrorAction SilentlyContinue
if (-not $p) { Write-Host "app not running"; exit 1 }
$wins = [PM]::Wins([uint32]$p.Id)
if ($wins.Count -eq 0) { Write-Host "no windows"; exit 1 }

$codes = @{ "space" = 0x20; "f1" = 0x70; "f5" = 0x74; "esc" = 0x1B; "enter" = 0x0D;
    "i" = 0x49; "o" = 0x4F; "q" = 0x51; "w" = 0x57; "tab" = 0x09; "n" = 0x4E; "del" = 0x2E }
if ($Keys -ne "") {
    $h = $wins[0]
    foreach ($k in $Keys.Split(",")) {
        $code = $codes[$k.Trim().ToLower()]
        if ($null -eq $code) { Write-Host "unknown key $k"; continue }
        [void][PM]::PostMessage($h, 0x0100, [IntPtr]$code, [IntPtr]0)
        Start-Sleep -Milliseconds 60
        [void][PM]::PostMessage($h, 0x0101, [IntPtr]$code, [IntPtr]0)
        Start-Sleep -Milliseconds 250
    }
}
Start-Sleep -Seconds $Wait
$wins = [PM]::Wins([uint32]$p.Id)
Write-Host "windows: $($wins.Count)"
$idx = if ($WinIndex -ge 0) { $WinIndex } else { $wins.Count - 1 }
$h = $wins[$idx]
$r = New-Object PM+R
[void][PM]::GetWindowRect($h, [ref]$r)
$w = $r.Rr - $r.L; $ht = $r.B - $r.T
Write-Host "capturing window $idx : ${w}x${ht}"
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][PM]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
$bmp.Save("$dest\$Shot", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $Shot"
