# שולח Ctrl+<אות> באמצעות keybd_event אמיתי (כלי פיתוח)
param([string]$Key = "T", [int]$Wait = 2, [string]$Shot = "poke3.png", [int]$WinIndex = 0)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public class KB{
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
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static void Focus(IntPtr h){
   uint cur = GetCurrentThreadId(); uint pid;
   uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
   AttachThreadInput(cur, fg, true); BringWindowToTop(h); SetForegroundWindow(h);
   AttachThreadInput(cur, fg, false); }
 public static void CtrlKey(byte vk){
   keybd_event(0x11, 0, 0, IntPtr.Zero);          // Ctrl down
   System.Threading.Thread.Sleep(40);
   keybd_event(vk, 0, 0, IntPtr.Zero);
   System.Threading.Thread.Sleep(40);
   keybd_event(vk, 0, 2, IntPtr.Zero);            // key up
   System.Threading.Thread.Sleep(40);
   keybd_event(0x11, 0, 2, IntPtr.Zero); }        // Ctrl up
}
"@
[void][KB]::SetProcessDPIAware()
$dest = "$env:TEMP\claude\D--Claude\a7f901d1-facb-465f-a4c9-637652910880\scratchpad"
$p = Get-Process SubtitleStudio -ErrorAction SilentlyContinue
if (-not $p) { Write-Host "not running"; exit 1 }
$wins = [KB]::Wins([uint32]$p.Id)
[KB]::Focus($wins[0])
Start-Sleep -Milliseconds 700
$vk = [byte][char]$Key.ToUpper()
[KB]::CtrlKey($vk)
Start-Sleep -Seconds $Wait
$wins = [KB]::Wins([uint32]$p.Id)
Write-Host "windows: $($wins.Count)"
$h = $wins[[Math]::Min($WinIndex, $wins.Count - 1)]
$r = New-Object KB+R
[void][KB]::GetWindowRect($h, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap(($r.Rr - $r.L), ($r.B - $r.T))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][KB]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
$bmp.Save("$dest\$Shot", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $Shot ($($r.Rr-$r.L)x$($r.B-$r.T))"
