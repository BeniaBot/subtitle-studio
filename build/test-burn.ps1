# בדיקת קצה-לקצה: פותח את התוכנה, מפעיל הטמעה בסרט, ובודק שהקובץ נוצר
$root = Split-Path $PSScriptRoot -Parent
$scratch = "$env:TEMP\ss-gallery"
$media = "$scratch\test.mp4"
$expected = "$scratch\test - עם כתוביות צרובות.mp4"
Remove-Item $expected -ErrorAction SilentlyContinue
Get-Process SubtitleStudio -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public class MS{
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
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Wins(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
     if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static void Focus(IntPtr h){ uint cur=GetCurrentThreadId(); uint pid;
   uint fg=GetWindowThreadProcessId(GetForegroundWindow(), out pid);
   AttachThreadInput(cur,fg,true); BringWindowToTop(h); SetForegroundWindow(h); AttachThreadInput(cur,fg,false); }
 public static void Click(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(120);
   mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0004,0,0,0,IntPtr.Zero); }
}
"@
[void][MS]::SetProcessDPIAware()

$p = Start-Process "$root\dist\SubtitleStudio.exe" -ArgumentList $media -PassThru
Start-Sleep -Seconds 8
$wins = [MS]::Wins([uint32]$p.Id)
[MS]::Focus($wins[0])
Start-Sleep -Milliseconds 700
[System.Windows.Forms.SendKeys]::SendWait("{F5}")
Start-Sleep -Seconds 2

$wins = [MS]::Wins([uint32]$p.Id)
if ($wins.Count -lt 2) { Write-Host "FAIL: export dialog did not open"; exit 1 }
$dlg = $wins[0]
$r = New-Object MS+R
[void][MS]::GetWindowRect($dlg, [ref]$r)
$w = $r.Rr - $r.L; $h = $r.B - $r.T
Write-Host "dialog at $($r.L),$($r.T) size ${w}x${h}"
# כפתור האישור: פינה שמאלית-תחתונה של הדיאלוג
$bx = $r.L + [int]($w * 0.20)
$by = $r.B - [int]($h * 0.075)
Write-Host "clicking OK at $bx,$by"
# הקלקה על חלון שאינו בחזית נבלעת בהפעלה - ממקדים קודם
[MS]::Focus($dlg)
Start-Sleep -Milliseconds 500
[MS]::Click($bx, $by)
Start-Sleep -Milliseconds 400
[MS]::Click($bx, $by)

# ממתינים לסיום הקידוד
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    if (Test-Path $expected) {
        $len = (Get-Item $expected).Length
        if ($len -gt 100000) { Write-Host "OUTPUT OK after $($i*2)s : $([math]::Round($len/1MB,2)) MB"; break }
    }
}
$wins = [MS]::Wins([uint32]$p.Id)
Write-Host "windows now: $($wins.Count)"
$hh = $wins[0]
[void][MS]::GetWindowRect($hh, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap(($r.Rr-$r.L), ($r.B-$r.T))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [void][MS]::PrintWindow($hh, $hdc, 2); $g.ReleaseHdc($hdc)
$bmp.Save("$scratch\burn_progress.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
if (Test-Path $expected) { Write-Host "FILE: $((Get-Item $expected).Length) bytes" } else { Write-Host "FILE MISSING" }
