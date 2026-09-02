# לחיצה במקום מסוים בחלון התוכנה (כלי פיתוח)
param([int]$X = 100, [int]$Y = 100, [int]$Wait = 2, [string]$Shot = "click.png", [int]$WinIndex = 0, [int]$ShotIndex = -1)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public class CK{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref P p);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
 [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,IntPtr e);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 [StructLayout(LayoutKind.Sequential)] public struct P{public int X,Y;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static void Focus(IntPtr h){ uint cur=GetCurrentThreadId(); uint pid;
   uint fg=GetWindowThreadProcessId(GetForegroundWindow(), out pid);
   AttachThreadInput(cur,fg,true); BringWindowToTop(h); SetForegroundWindow(h); AttachThreadInput(cur,fg,false); }
 public static void Click(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(120);
   mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(70); mouse_event(0x0004,0,0,0,IntPtr.Zero); }
}
"@
[void][CK]::SetProcessDPIAware()
$dest = "$env:TEMP\claude\D--Claude\a7f901d1-facb-465f-a4c9-637652910880\scratchpad"
$p = Get-Process SubtitleStudio -ErrorAction SilentlyContinue
if (-not $p) { Write-Host "not running"; exit 1 }
$wins = [CK]::Wins([uint32]$p.Id)
$h = $wins[[Math]::Min($WinIndex, $wins.Count - 1)]
[CK]::Focus($h)
Start-Sleep -Milliseconds 500
$c = New-Object CK+P
[void][CK]::ClientToScreen($h, [ref]$c)
[CK]::Click(($c.X + $X), ($c.Y + $Y))
Start-Sleep -Seconds $Wait
$wins = [CK]::Wins([uint32]$p.Id)
Write-Host "windows: $($wins.Count)"
$idx = if ($ShotIndex -ge 0) { [Math]::Min($ShotIndex, $wins.Count - 1) } else { 0 }
$t = $wins[$idx]
$r = New-Object CK+R
[void][CK]::GetWindowRect($t, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap(($r.Rr - $r.L), ($r.B - $r.T))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][CK]::PrintWindow($t, $hdc, 2); $g.ReleaseHdc($hdc)
$bmp.Save("$dest\$Shot", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $Shot ($($r.Rr-$r.L)x$($r.B-$r.T))"
