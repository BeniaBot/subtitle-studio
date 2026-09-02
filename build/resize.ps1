# משנה את גודל חלון התוכנה ומצלם - לבדיקת פריסה במסכים קטנים
param([int]$W = 1366, [int]$H = 768, [string]$Shot = "resize.png", [int]$Wait = 2)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Collections.Generic;using System.Text;using System.Runtime.InteropServices;
public class RS{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int ht,bool rp);
 [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr h, ref WP p);
 [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref WP p);
 [StructLayout(LayoutKind.Sequential)] public struct PT{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] public struct WP{
   public int length, flags, showCmd; public PT minPos, maxPos; public R normalPos; }
 public static void SetRect(IntPtr h,int x,int y,int w,int ht){
   WP p = new WP(); p.length = Marshal.SizeOf(typeof(WP));
   GetWindowPlacement(h, ref p);
   p.showCmd = 1;                     // SW_SHOWNORMAL
   p.normalPos.L = x; p.normalPos.T = y; p.normalPos.Rr = x + w; p.normalPos.B = y + ht;
   SetWindowPlacement(h, ref p);
 }
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
}
"@
[void][RS]::SetProcessDPIAware()
$dest = "$env:TEMP\ss-gallery"
New-Item -ItemType Directory -Force $dest | Out-Null

$p = Get-Process SubtitleStudio -ErrorAction SilentlyContinue
if (-not $p) { Write-Host "not running"; exit 1 }

# החלון הגדול ביותר מסוג WinForms הוא הראשי
$best = [IntPtr]::Zero; $bestArea = -1
foreach ($win in [RS]::Wins([uint32]$p.Id)) {
    $c = New-Object Text.StringBuilder 256
    [void][RS]::GetClassName($win, $c, 256)
    if ($c.ToString() -notlike "WindowsForms*") { continue }
    $r = New-Object RS+R; [void][RS]::GetWindowRect($win, [ref]$r)
    $a = ($r.Rr - $r.L) * ($r.B - $r.T)
    if ($a -gt $bestArea) { $bestArea = $a; $best = $win }
}
if ($best -eq [IntPtr]::Zero) { Write-Host "no window"; exit 1 }
if ([RS]::IsIconic($best)) { [void][RS]::ShowWindow($best, 9); Start-Sleep -Milliseconds 700 }
# MoveWindow פשוט - עובד כשהחלון לא ממוקסם וגם לא ממוזער
if ([RS]::IsZoomed($best)) { [void][RS]::ShowWindow($best, 9); Start-Sleep -Milliseconds 600 }
[void][RS]::MoveWindow($best, 60, 30, $W, $H, $true)
Start-Sleep -Seconds $Wait

$r = New-Object RS+R; [void][RS]::GetWindowRect($best, [ref]$r)
$sw = $r.Rr - $r.L; $sh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $sw, $sh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][RS]::PrintWindow($best, $hdc, 2); $g.ReleaseHdc($hdc)
$path = Join-Path $dest $Shot
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host ("saved {0} ({1}x{2})" -f $path, $sw, $sh)
