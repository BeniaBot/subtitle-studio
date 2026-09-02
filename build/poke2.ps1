# שולח קיצור עם Ctrl לחלון הרץ (כלי פיתוח) ומצלם
param([string]$Send = "^t", [int]$Wait = 2, [string]$Shot = "poke2.png", [int]$WinIndex = 0)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public class FG{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
 [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static void Focus(IntPtr h){
   uint cur = GetCurrentThreadId();
   uint fgPid; uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
   AttachThreadInput(cur, fg, true);
   BringWindowToTop(h); SetForegroundWindow(h);
   AttachThreadInput(cur, fg, false);
 }
}
"@
[void][FG]::SetProcessDPIAware()
$dest = "$env:TEMP\claude\D--Claude\a7f901d1-facb-465f-a4c9-637652910880\scratchpad"
$p = Get-Process SubtitleStudio -ErrorAction SilentlyContinue
if (-not $p) { Write-Host "not running"; exit 1 }
$wins = [FG]::Wins([uint32]$p.Id)
[FG]::Focus($wins[0])
Start-Sleep -Milliseconds 600
if ($Send -ne "") { [System.Windows.Forms.SendKeys]::SendWait($Send) }
Start-Sleep -Seconds $Wait
$wins = [FG]::Wins([uint32]$p.Id)
Write-Host "windows: $($wins.Count)"
$h = $wins[[Math]::Min($WinIndex, $wins.Count - 1)]
$r = New-Object FG+R
[void][FG]::GetWindowRect($h, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap(($r.Rr - $r.L), ($r.B - $r.T))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][FG]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
$bmp.Save("$dest\$Shot", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $Shot ($($r.Rr-$r.L)x$($r.B-$r.T))"
