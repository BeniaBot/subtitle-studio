# installer-shots.ps1 - חלונות המתקין והמסיר לתמונות, מחוץ למסך, בלי להתקין או להסיר כלום (0.8.33: המתקין גם באנגלית).
# טוען את dist\Subtext-Setup.exe כאסמבלי, בונה את החלונות ומעביר אותם בין המסכים דרך השיטות שלהם (Finish, Done).
#   installer-shots.ps1 [-Lang he|en] [-Out dir]
# הפלט: <Out>\setup-0-<lang>.png (בחירה), setup-1 (מתקין), setup-2 (סיום), uninstall-0, uninstall-2   (ברירת מחדל: %TEMP%\ss-installer)
param([string]$Lang = 'he', [string]$Out = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Runtime.InteropServices;
public static class InstShot {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    public struct RECT { public int L, T, R, B; }
    public static Bitmap Grab(IntPtr h) {
        RECT w; GetWindowRect(h, out w);
        RECT v; if (DwmGetWindowAttribute(h, 9, out v, Marshal.SizeOf(typeof(RECT))) != 0) v = w;
        Bitmap full = new Bitmap(w.R - w.L, w.B - w.T);
        using (Graphics g = Graphics.FromImage(full)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
        Bitmap b = full.Clone(new Rectangle(v.L - w.L, v.T - w.T, v.R - v.L, v.B - v.T), full.PixelFormat);
        full.Dispose(); return b;
    }
}
'@
[void][InstShot]::SetProcessDPIAware()
[System.Windows.Forms.Application]::EnableVisualStyles()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext-Setup.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudioSetup.$n") }
function Pump([int]$ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }
if ($Out -eq '') { $Out = Join-Path $env:TEMP 'ss-installer' }
New-Item -ItemType Directory -Force $Out | Out-Null

[void](TY 'Skin').GetMethod('Init', $ST).Invoke($null, @())
(TY 'T').GetField('En', $ST).SetValue($null, ($Lang -eq 'en'))
$dir = 'C:\Users\User\AppData\Local\Programs\Subtext'

function Snap($f, $name) {
    $f.Refresh(); Pump 250
    $bmp = [InstShot]::Grab($f.Handle); $file = Join-Path $Out "$name-$Lang.png"; $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host ('  ' + $file)
}
function Show($f) { $f.StartPosition = 'Manual'; $f.Location = New-Object Drawing.Point -4000, 0; $f.ShowInTaskbar = $false; $f.Show(); Pump 400 }

# ---- המתקין: בחירה, באמצע, סיום ----
$opt = [Activator]::CreateInstance((TY 'Options')); $opt.Dir = $dir
$sf = [Activator]::CreateInstance((TY 'SetupForm'), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, @($opt), $null)
Show $sf
Snap $sf 'setup-0'
# באמצע: מה ש-Start עושה, בלי להפעיל את העבודה
$sfT = TY 'SetupForm'
$sfT.GetField('_page', $IN).SetValue($sf, 1); $sfT.GetField('_doneDir', $IN).SetValue($sf, $dir)
$sfT.GetField('_status', $IN).SetValue($sf, $(if ($Lang -eq 'en') { 'Copying Subtext…' } else { 'מעתיק את התוכנה…' }))
foreach ($n in '_path', '_browse', '_desk', '_cancel') { $sfT.GetField($n, $IN).GetValue($sf).Visible = $false }
$bar = $sfT.GetField('_bar', $IN).GetValue($sf); $bar.Visible = $true; $bar.Value = 0.45
$okb = $sfT.GetField('_ok', $IN).GetValue($sf); $okb.Enabled = $false; $okb.Text = $(if ($Lang -eq 'en') { 'Installing…' } else { 'מתקין…' })
$sf.Invalidate(); Snap $sf 'setup-1'
$job = [Activator]::CreateInstance((TY 'Job')); $job.Dir = $dir; $job.ShortcutStart = $true; $job.ShortcutDesktop = $true
[void]$sfT.GetMethod('Finish', $IN).Invoke($sf, @($job, $true))
Snap $sf 'setup-2'
$sfT.GetField('_page', $IN).SetValue($sf, 0); $sf.Close(); $sf.Dispose()

# ---- המסיר: שאלה, סיום ----
$uf = [Activator]::CreateInstance((TY 'UninstallForm'), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, @([string]$dir), $null)
Show $uf
Snap $uf 'uninstall-0'
# מה ש-OnPrimary עושה לפני ההסרה (מסתיר את תיבת הסימון ואת ״ביטול״), ואז הסיום
$ufT = TY 'UninstallForm'
foreach ($n in '_engine', '_cancel') { $ufT.GetField($n, $IN).GetValue($uf).Visible = $false }
[void]$ufT.GetMethod('Done', $IN).Invoke($uf, @($true, [string]''))
Snap $uf 'uninstall-2'
Write-Host ('  page=' + $ufT.GetField('_page', $IN).GetValue($uf))
$uf.Close(); $uf.Dispose()
