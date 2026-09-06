# פותח כל חלון דיאלוג של התוכנה ומצלם אותו (ביקורת עיצוב)
param([string]$Only = "")
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
# תיקיית פלט קבועה - לא תלויה במספר הסשן
$dest = "$env:TEMP\ss-gallery"
New-Item -ItemType Directory -Force $dest | Out-Null
$scratch = $dest

# קובץ דוגמה - נוצר פעם אחת עם ffmpeg המוטמע
if (-not (Test-Path "$scratch\test.mp4")) {
    $ff = "$env:LOCALAPPDATA\SubtitleStudio\runtime\ffmpeg.exe"
    if (-not (Test-Path $ff)) { $ff = "D:\Claude\subtitle-studio\tools\ffmpeg.exe" }
    & $ff -y -f lavfi -i "testsrc=size=640x360:rate=25:duration=40" `
          -f lavfi -i "sine=frequency=440:duration=40" `
          -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest "$scratch\test.mp4" 2>$null | Out-Null
}

Add-Type @"
using System;using System.Runtime.InteropServices;
public class GW{
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
}
"@
[void][GW]::SetProcessDPIAware()

$asm = [Reflection.Assembly]::LoadFrom("D:\Claude\subtitle-studio\dist\SubtitleStudio.exe")
$NP = [Reflection.BindingFlags]::NonPublic
$PB = [Reflection.BindingFlags]::Public
$ST = [Reflection.BindingFlags]::Static
$IN = [Reflection.BindingFlags]::Instance
$CI = [Reflection.BindingFlags]::CreateInstance

function T($n) { $asm.GetType("SubtitleStudio.$n") }

# פורסים את המנוע המוטמע כמו בהפעלה אמיתית. בלי זה Ff.Exe נופל ל-PATH,
# וחלון "על התוכנה" מצלם נתיב של ffmpeg זר שמותקן במחשב הפיתוח.
(T "Runtime").GetMethod("Prepare", $NP -bor $PB -bor $ST).Invoke($null, @())
function NewOf($n, $argv) {
    [Activator]::CreateInstance((T $n), ($NP -bor $PB -bor $IN -bor $CI), $null, $argv, $null)
}

# סקאלה לפי המסך
$theme = T "Theme"
$bmp = New-Object System.Drawing.Bitmap 1,1
$g = [System.Drawing.Graphics]::FromImage($bmp)
$theme.GetField("Scale", $NP -bor $PB -bor $ST).SetValue($null, [float]($g.DpiX / 96))
$g.Dispose(); $bmp.Dispose()

# מסמך לדוגמה
$doc = NewOf "Doc" @()
$cuesField = (T "Doc").GetField("Cues", $NP -bor $PB -bor $IN)
$cues = $cuesField.GetValue($doc)
foreach ($c in @(@(1000,4000,"שלום לכולם"), @(5500,9000,"This is a test line"), @(11000,15000,"הכתובית השלישית"))) {
    $cue = NewOf "Cue" @([int64]$c[0], [int64]$c[1], [string]$c[2])
    $cues.Add($cue)
}
# מידע מדיה אמיתי
$ff = T "Ff"
$mi = $ff.GetMethod("ProbeFile", $NP -bor $PB -bor $ST).Invoke($null, @("$scratch\test.mp4"))
$style = NewOf "SubStyle" @()

$dialogs = @(
    @{ n = "ImportText"; make = { NewOf "ImportTextDlg" @([int64]0) } },
    @{ n = "Fix";        make = { NewOf "FixDlg" @($doc) } },
    @{ n = "Sync";       make = { $cues[1].Selected = $true; NewOf "SyncDlg" @($doc, [int64]7900, (NewOf "SyncState" @())) } },
    @{ n = "Trim";       make = { NewOf "TrimDlg" @($null, $mi, $doc, [int64]5000, [int64]15000) } },
    @{ n = "Extract";    make = { NewOf "ExtractSubsDlg" @($null, $mi) } },
    @{ n = "Tools";      make = { NewOf "ToolsDlg" @($null, $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = "FitSize";    make = {
        $all = (T "MediaTools").GetMethod("All", $NP -bor $PB -bor $ST).Invoke($null, @())
        $tool = $null
        foreach ($x in $all) { if ((T "MediaTool").GetField("Name", $NP -bor $PB -bor $IN).GetValue($x) -like "*גודל קובץ*") { $tool = $x } }
        NewOf "ToolRunDlg" @($null, $tool, $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = "VolumeTool"; make = {
        $all = (T "MediaTools").GetMethod("All", $NP -bor $PB -bor $ST).Invoke($null, @())
        $tool = $all[0]
        NewOf "ToolRunDlg" @($null, $tool, $mi, [int64]-1, [int64]-1, [int64]0) } },    @{ n = "Help";       make = { NewOf "HelpDlg" @() } },
    @{ n = "Export";     make = { NewOf "ExportVideoDlg" @($null, $doc, $mi, $style, [int64]-1, [int64]-1) } },
    @{ n = "About";      make = { NewOf "AboutDlg" @() } },
    @{ n = "AiSetup";    make = { NewOf "AiSetupDlg" @() } },
    @{ n = "AiTranslate"; make = { NewOf "AiTranslateDlg" @($doc) } },
    @{ n = "Reverse";    make = {
        $all = (T "MediaTools").GetMethod("All", $NP -bor $PB -bor $ST).Invoke($null, @())
        $tool = $null
        foreach ($x in $all) { if ((T "MediaTool").GetField("Name", $NP -bor $PB -bor $IN).GetValue($x) -like "*ריוורס*") { $tool = $x } }
        NewOf "ToolRunDlg" @($null, $tool, $mi, [int64]-1, [int64]-1, [int64]0) } }
)

foreach ($d in $dialogs) {
    if ($Only -ne "" -and $d.n -ne $Only) { continue }
    try {
        $f = & $d.make
        $f.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
        $f.Show()
        $f.Refresh()
        Start-Sleep -Milliseconds 700
        $r = New-Object GW+R
        [void][GW]::GetWindowRect($f.Handle, [ref]$r)
        $w = $r.Rr - $r.L; $h = $r.B - $r.T
        $b = New-Object System.Drawing.Bitmap($w, $h)
        $gg = [System.Drawing.Graphics]::FromImage($b)
        $hdc = $gg.GetHdc(); [void][GW]::PrintWindow($f.Handle, $hdc, 2); $gg.ReleaseHdc($hdc)
        $b.Save("$dest\$($d.n).png", [System.Drawing.Imaging.ImageFormat]::Png)
        $gg.Dispose(); $b.Dispose()
        Write-Host ("{0,-12} {1}x{2}" -f $d.n, $w, $h)
        $f.Close(); $f.Dispose()
    } catch {
        Write-Host ("{0,-12} FAILED: {1}" -f $d.n, $_.Exception.InnerException.Message)
    }
}
Write-Host "gallery: $dest"
