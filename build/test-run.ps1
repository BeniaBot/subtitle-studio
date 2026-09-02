# בנייה + הרצה + צילום מסך של החלון (לבדיקות פיתוח בלבד)
param([int]$Wait = 7, [string]$Shot = "shot.png", [string]$Media = "")
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Get-Process SubtitleStudio -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
Remove-Item "$env:TEMP\SubStudio-error.txt" -ErrorAction SilentlyContinue

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$res = if (Test-Path "build\payload\ffmpeg.pack") { '/resource:build\payload\ffmpeg.pack,ffmpeg.pack' } else { '/nowarn:0' }
$out = & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 /out:"dist\SubtitleStudio.exe" `
    /win32icon:"build\app.ico" /win32manifest:"build\app.manifest" $res `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    src\*.cs 2>&1
$errs = $out | Where-Object { $_ -match ': error ' }
if ($errs) { $errs | Select-Object -First 30; exit 1 }
Write-Host "BUILD OK"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class WW{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref P p);
 [StructLayout(LayoutKind.Sequential)] public struct P{public int X,Y;}
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){
   var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero);
   return res; }
 public static string Title(IntPtr h){ var sb=new StringBuilder(300); GetWindowTextW(h,sb,300); return sb.ToString(); }
}
"@ -ErrorAction SilentlyContinue

[void][WW]::SetProcessDPIAware()
$args2 = @()
if ($Media -ne "") { $args2 += $Media }
$p = if ($args2.Count -gt 0) { Start-Process "dist\SubtitleStudio.exe" -ArgumentList $args2 -PassThru } else { Start-Process "dist\SubtitleStudio.exe" -PassThru }
Start-Sleep -Seconds $Wait
$p.Refresh()
if ($p.HasExited) {
    Write-Host "CRASHED exit=$($p.ExitCode)"
    if (Test-Path "$env:TEMP\SubStudio-error.txt") { Get-Content "$env:TEMP\SubStudio-error.txt" -TotalCount 25 }
    exit 2
}
$wins = [WW]::Wins([uint32]$p.Id)
Write-Host "windows: $($wins.Count)"
foreach ($h in $wins) { Write-Host ("  {0} : {1}" -f $h, [WW]::Title($h)) }
if ($wins.Count -eq 0) { Write-Host "NO WINDOW"; exit 3 }
$h = $wins[$wins.Count - 1]
$r = New-Object WW+R
[void][WW]::GetWindowRect($h, [ref]$r)
$w = $r.Rr - $r.L; $ht = $r.B - $r.T
if ($w -le 0 -or $ht -le 0) { Write-Host "bad rect"; exit 4 }
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][WW]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$dest = Join-Path "$env:TEMP\claude\D--Claude\a7f901d1-facb-465f-a4c9-637652910880\scratchpad" $Shot
$bmp.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "SHOT $dest ($w x $ht)"
