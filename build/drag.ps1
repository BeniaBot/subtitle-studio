# גרירת עכבר אמיתית בתוך חלון התוכנה, ואז צילום (כלי פיתוח)
# דוגמה:  drag.ps1 -X1 200 -Y1 880 -X2 420 -Y2 880 -Type "שלום" -Shot drag.png
param(
    [int]$X1 = 100, [int]$Y1 = 100,
    [int]$X2 = 300, [int]$Y2 = 100,
    [int]$Steps = 14,
    [int]$Wait = 2,
    [string]$Type = "",
    [string]$Shot = "drag.png",
    [int]$WinIndex = -1,
    [ValidateSet("main", "dialog")] [string]$Target = "main"
)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public class DG{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref P p);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
 [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
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
 public static void Down(){ mouse_event(0x0002,0,0,0,IntPtr.Zero); }
 public static void Up(){ mouse_event(0x0004,0,0,0,IntPtr.Zero); }
}
"@
[void][DG]::SetProcessDPIAware()

$dest = "$env:TEMP\ss-gallery"
New-Item -ItemType Directory -Force $dest | Out-Null

$p = Get-Process SubtitleStudio -ErrorAction SilentlyContinue
if (-not $p) { Write-Host "not running"; exit 1 }
$wins = [DG]::Wins([uint32]$p.Id)
if ($wins.Count -eq 0) { Write-Host "no window"; exit 1 }

# סדר המנייה משתנה בין קריאות - מזהים לפי כותרת:
# לחלון הראשי יש כותרת, לדיאלוגים אין
$h = [IntPtr]::Zero
if ($WinIndex -ge 0) {
    $h = $wins[[Math]::Min($WinIndex, $wins.Count - 1)]
} else {
    # רק חלונות WinForms אמיתיים (טולטיפ הוא חלון נפרד עם כותרת משלו), והגדול = הראשי
    $real = @()
    foreach ($w in $wins) {
        $c = New-Object System.Text.StringBuilder 256
        [void][DG]::GetClassName($w, $c, 256)
        if ($c.ToString() -notlike "WindowsForms*") { continue }
        $rr = New-Object DG+R
        [void][DG]::GetWindowRect($w, [ref]$rr)
        $real += [pscustomobject]@{ H = $w; A = ($rr.Rr - $rr.L) * ($rr.B - $rr.T) }
    }
    $real = $real | Sort-Object -Property A -Descending
    if ($real.Count -eq 0) { Write-Host "no window"; exit 1 }
    if ($Target -eq "main") { $h = $real[0].H }
    elseif ($real.Count -gt 1) { $h = $real[1].H }
    else { Write-Host "no dialog window"; exit 1 }
}
# חלון ממוזער מחזיר מלבן (-32000) - משחזרים לפני כל פעולה
if ([DG]::IsIconic($h)) { [void][DG]::ShowWindow($h, 9); Start-Sleep -Milliseconds 700 }
[DG]::Focus($h)
Start-Sleep -Milliseconds 400

# הקואורדינטות הן של תמונת החלון (כמו בצילומים) - לכן מודדים מפינת החלון ולא מהלקוח
$wr = New-Object DG+R
[void][DG]::GetWindowRect($h, [ref]$wr)
$o = New-Object DG+P
$o.X = $wr.L; $o.Y = $wr.T

[void][DG]::SetCursorPos($o.X + $X1, $o.Y + $Y1)
Start-Sleep -Milliseconds 150
[DG]::Down()
for ($i = 1; $i -le $Steps; $i++) {
    $x = $X1 + ($X2 - $X1) * $i / $Steps
    $y = $Y1 + ($Y2 - $Y1) * $i / $Steps
    [void][DG]::SetCursorPos($o.X + [int]$x, $o.Y + [int]$y)
    Start-Sleep -Milliseconds 22
}
Start-Sleep -Milliseconds 120
[DG]::Up()
Start-Sleep -Milliseconds 400

if ($Type -ne "") {
    # SendKeys עם עברית שולח Alt+numpad ומפעיל את תפריט המערכת - מדביקים מהלוח
    [System.Windows.Forms.Clipboard]::SetText($Type)
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait("^v")
    Start-Sleep -Milliseconds 500
}

Start-Sleep -Seconds $Wait
$r = New-Object DG+R
[void][DG]::GetWindowRect($h, [ref]$r)
$w = $r.Rr - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][DG]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$path = Join-Path $dest $Shot
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host ("saved {0} ({1}x{2})" -f $path, $w, $ht)
