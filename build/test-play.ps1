# בדיקת ניגון: מפעיל, לוחץ רווח, ומצלם אחרי כמה שניות
param([string]$Media = "")
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
Get-Process SubtitleStudio -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class PW{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){
   var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero);
   return res; }
}
"@ -ErrorAction SilentlyContinue
[void][PW]::SetProcessDPIAware()

$dest = "$env:TEMP\claude\D--Claude\a7f901d1-facb-465f-a4c9-637652910880\scratchpad"
$p = Start-Process "dist\SubtitleStudio.exe" -ArgumentList $Media -PassThru
Start-Sleep -Seconds 7

function Shot($name) {
    $wins = [PW]::Wins([uint32]$p.Id)
    if ($wins.Count -eq 0) { Write-Host "no window"; return }
    $h = $wins[$wins.Count - 1]
    $r = New-Object PW+R
    [void][PW]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap(($r.Rr - $r.L), ($r.B - $r.T))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc(); [void][PW]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
    $bmp.Save("$dest\$name", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "shot $name"
}

$wins = [PW]::Wins([uint32]$p.Id)
[void][PW]::SetForegroundWindow($wins[$wins.Count - 1])
Start-Sleep -Milliseconds 800
[System.Windows.Forms.SendKeys]::SendWait(" ")
Start-Sleep -Seconds 4
Shot "play1.png"
Start-Sleep -Seconds 3
Shot "play2.png"
[System.Windows.Forms.SendKeys]::SendWait(" ")
Start-Sleep -Milliseconds 500
Write-Host "done"
