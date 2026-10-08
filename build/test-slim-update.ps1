# test-slim-update.ps1 - העדכון הקטן (0.8.8) מקצה לקצה: התוכנה האמיתית, מנגנון העדכון שלה, ושרת מדומה
# על המחשב הזה (slim-server.ps1) שמתחזה ל-API של גיטהאב ולקבצי המהדורה.
#
#   powershell -ExecutionPolicy Bypass -File build\test-slim-update.ps1
#
# בונה מהקוד הנוכחי שתי גרסאות (0.8.98 ״ישנה״, 0.8.99 ״חדשה״) עם אותו מנוע, ועוד ״חדשה״ עם מנוע אחר, ומריץ:
#  A. עדכון רגיל: ההצעה אומרת כמה יורד, יורד **רק** הקובץ הקטן (יומן השרת), הקובץ שהוחלף זהה בדיוק לחדש,
#     הגרסה החדשה עולה, והמנוע **לא** נפרס מחדש.
#  B. מנוע אחר בגרסה החדשה: הקטן לא מסתדר, ונופלים להורדה המלאה - והתוצאה עדיין זהה בדיוק.
#  C. ״ביטול״ באמצע הורדה איטית: התוכנה הישנה ממשיכה לרוץ, לא הוחלפה, ולא נשאר קובץ זמני.
# מצלם ל-D:\Claude\_ss-slim\shots. משתנה הסביבה SUBSTUDIO_UPDATE_API מכוון את התוכנה לשרת - והתוכנה מקבלת
# ממנו רק כתובת על המחשב הזה (Updater.LocalApi).
param([switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$base = 'D:\Claude\_ss-slim'
$shots = Join-Path $base 'shots'
Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $base, $shots -Force | Out-Null
$script:failures = 0
function Log($s) { Write-Host ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $s) }
function Fail($s) { $script:failures++; Log ("!! " + $s) }

# ---- בנייה: עותק של הקוד, עם מספר גרסה אחר ----
function Variant($name, $version, $ffmpeg) {
    $dir = Join-Path $base "src-$name"
    $exe = Join-Path $dir 'dist\Subtext.exe'
    if ((Test-Path $exe) -and -not $Rebuild) { return $dir }
    if (Test-Path $dir) { cmd /c "rmdir /s /q `"$dir`"" }
    New-Item -ItemType Directory (Join-Path $dir 'build\payload') -Force | Out-Null
    foreach ($d in 'src', 'assets', 'licenses') { Copy-Item (Join-Path $repo $d) (Join-Path $dir $d) -Recurse }
    foreach ($f in 'build.cmd', 'LICENSE', 'build\make-payload.ps1', 'build\make-overlay.ps1', 'build\make-icon.ps1', 'build\app.manifest', 'build\app.ico') {
        Copy-Item (Join-Path $repo $f) (Join-Path $dir $f)
    }
    if ($ffmpeg) {
        # מנוע אחר: האריזה תיעשה מחדש מה-ffmpeg הזה
        New-Item -ItemType Directory (Join-Path $dir 'tools') -Force | Out-Null
        Copy-Item $ffmpeg (Join-Path $dir 'tools\ffmpeg.exe')
    } else {
        Copy-Item (Join-Path $repo 'build\payload\ffmpeg.pack') (Join-Path $dir 'build\payload')
    }
    $u = Join-Path $dir 'src\Updater.cs'
    $t = [IO.File]::ReadAllText($u)
    $t = [regex]::Replace($t, 'public const string Version = "[^"]+";', 'public const string Version = "' + $version + '";')
    [IO.File]::WriteAllText($u, $t, (New-Object Text.UTF8Encoding $false))
    Log "building $name ($version)..."
    cmd /c (Join-Path $dir 'build.cmd') | Select-Object -Last 1 | ForEach-Object { Log ("  " + $_) }
    if (-not (Test-Path $exe)) { throw "build of $name failed" }
    return $dir
}
$old = Variant 'old' '0.8.98' $null
$new = Variant 'new' '0.8.99' $null
$other = Join-Path $base 'ffmpeg-other.exe'
if (-not (Test-Path $other)) { Copy-Item 'D:\Claude\ffmpeg-dl\ffmpeg-9.0.1.exe' $other }
$newB = Variant 'new-engine' '0.8.99' $other

# ---- חלונות: לצלם וללחוץ בלי לגנוב את המקלדת (כמו test-upgrade-real) ----
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class SW{
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, Cb c, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}
 public static List<IntPtr> Tops(uint pid){ var res=new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p); if(p==pid && IsWindowVisible(h)) res.Add(h); return true;}, IntPtr.Zero); return res; }
 public static IntPtr ChildWithText(IntPtr parent, string text){ IntPtr found=IntPtr.Zero;
   EnumChildWindows(parent, delegate(IntPtr h, IntPtr l){ var sb=new StringBuilder(300); GetWindowTextW(h,sb,300); if(sb.ToString()==text){ found=h; return false;} return true;}, IntPtr.Zero); return found; }
}
"@
[void][SW]::SetProcessDPIAware()
function Shot($h, $file) {
    $r = New-Object SW+R; [void][SW]::GetWindowRect($h, [ref]$r)
    if ($r.Rr - $r.L -le 0) { return }
    $bmp = New-Object Drawing.Bitmap ($r.Rr - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][SW]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($file); $bmp.Dispose()
}
function ClickBtn($dlg, $btn) {
    [void][SW]::SetWindowPos($dlg, [IntPtr](-1), 0, 0, 0, 0, 0x0013)    # TOPMOST, בלי הפעלה
    Start-Sleep -Milliseconds 250
    $cr = New-Object SW+R; [void][SW]::GetClientRect($btn, [ref]$cr)
    $lp = [IntPtr](([int]($cr.B / 2) -shl 16) -bor [int]($cr.Rr / 2))
    [void][SW]::PostMessage($btn, 0x0201, [IntPtr]1, $lp); Start-Sleep -Milliseconds 80
    [void][SW]::PostMessage($btn, 0x0202, [IntPtr]0, $lp)
}
function FindBtn($pid0, $text) {
    foreach ($h in [SW]::Tops([uint32]$pid0)) { $b = [SW]::ChildWithText($h, $text); if ($b -ne [IntPtr]::Zero) { return @($h, $b) } }
    return $null
}
function FreePort { $t = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), 0; $t.Start(); $p = $t.LocalEndpoint.Port; $t.Stop(); return $p }
function Sha($f) { return (Get-FileHash $f -Algorithm SHA256).Hash }

# ---- תרחיש ----
function Scenario($label, $srcOld, $srcNew, [int]$kbps, [bool]$cancel, [bool]$expectSlim) {
    Log "=== $label ==="
    $relDir = Join-Path $base "rel-$label"; $appDir = Join-Path $base "app-$label"
    foreach ($d in $relDir, $appDir) { if (Test-Path $d) { cmd /c "rmdir /s /q `"$d`"" }; New-Item -ItemType Directory $d -Force | Out-Null }
    Copy-Item (Join-Path $srcNew 'dist\Subtext.exe') (Join-Path $relDir 'Subtext.exe')
    Copy-Item (Join-Path $srcNew 'dist\Subtext-app-update.gz') (Join-Path $relDir 'Subtext-app-update.gz')
    $port = FreePort
    $dl = "http://127.0.0.1:$port/BeniaBot/subtitle-studio/releases/download/v0.8.99/"
    $assets = foreach ($n in 'Subtext-app-update.gz', 'Subtext.exe') {
        $f = Join-Path $relDir $n
        '{"name":"' + $n + '","size":' + (Get-Item $f).Length + ',"digest":"sha256:' + (Sha $f).ToLower() + '","browser_download_url":"' + $dl + $n + '"}'
    }
    $json = '{"tag_name":"v0.8.99","name":"0.8.99","body":"## בדיקה\n\nגרסת בדיקה של העדכון הקטן.","assets":[' + ($assets -join ',') + ']}'
    $jsonFile = Join-Path $relDir 'latest.json'
    [IO.File]::WriteAllText($jsonFile, $json, (New-Object Text.UTF8Encoding $false))
    $logFile = Join-Path $relDir 'server.log'; $stopFile = Join-Path $relDir 'stop'
    $srv = Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'slim-server.ps1'),
        '-Port', $port, '-Root', $relDir, '-Json', $jsonFile, '-Log', $logFile, '-KBps', $kbps, '-Stop', $stopFile) -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 800

    $appExe = Join-Path $appDir 'Subtext.exe'
    Copy-Item (Join-Path $srcOld 'dist\Subtext.exe') $appExe
    Set-Content (Join-Path $appDir 'portable.txt') 'portable' -Encoding ASCII
    $oldHash = Sha $appExe
    $newHash = Sha (Join-Path $relDir 'Subtext.exe')
    Get-ChildItem $env:TEMP -Filter 'Subtext-*' -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Subtext-0.8.99*' -or $_.Name -like 'Subtext-app-0.8.99*' } | ForEach-Object { [IO.File]::Delete($_.FullName) }

    $env:SUBSTUDIO_UPDATE_API = "http://127.0.0.1:$port/repos/BeniaBot/subtitle-studio/releases/latest"
    Remove-Item Env:\SUBSTUDIO_TEST -ErrorAction SilentlyContinue
    $p = Start-Process $appExe -PassThru
    Log ("old 0.8.98 started pid=" + $p.Id)
    try {
        $hit = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt 60 -and -not $hit) {
            Start-Sleep -Milliseconds 400; $p.Refresh(); if ($p.HasExited) { Fail "$label : the old version exited"; return }
            $hit = FindBtn $p.Id 'לעדכן עכשיו'
        }
        if (-not $hit) { Fail "$label : no update offer within 60s"; return }
        Log ("offer after " + [int]$sw.Elapsed.TotalSeconds + "s")
        $engine = Join-Path $appDir 'runtime\ffmpeg.exe'
        $engineTime = if (Test-Path $engine) { (Get-Item $engine).LastWriteTimeUtc } else { $null }
        Start-Sleep -Milliseconds 600
        Shot $hit[0] (Join-Path $shots "$label-1-offer.png")
        ClickBtn $hit[0] $hit[1]
        Log "clicked 'update now'"

        if ($cancel) {
            $c = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
            while ($sw.Elapsed.TotalSeconds -lt 20 -and -not $c) { Start-Sleep -Milliseconds 200; $c = FindBtn $p.Id 'ביטול' }
            if (-not $c) { Fail "$label : no download window with 'cancel'"; return }
            Start-Sleep -Milliseconds 1200
            Shot $c[0] (Join-Path $shots "$label-2-downloading.png")
            ClickBtn $c[0] $c[1]
            Log "clicked 'cancel'"
            Start-Sleep -Seconds 3
            $p.Refresh()
            $leftovers = @(Get-ChildItem $env:TEMP -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Subtext-0.8.99*' -or $_.Name -like 'Subtext-app-0.8.99*' })
            if ($p.HasExited) { Fail "$label : the program closed after cancel" }
            if ((Sha $appExe) -ne $oldHash) { Fail "$label : the program file changed after cancel" }
            if ($leftovers.Count -gt 0) { Fail ("$label : temp files left: " + ($leftovers.Name -join ', ')) }
            if (-not $p.HasExited -and (Sha $appExe) -eq $oldHash -and $leftovers.Count -eq 0) { Log "CANCEL OK: still running, unchanged, no temp files" }
            return
        }

        # ההורדה: חלון עם ״ביטול״ - מצלמים אותו פעם אחת
        $sw = [Diagnostics.Stopwatch]::StartNew(); $shotDl = $false
        while ($sw.Elapsed.TotalSeconds -lt 600) {
            $p.Refresh(); if ($p.HasExited) { break }
            if (-not $shotDl) { $c = FindBtn $p.Id 'ביטול'; if ($c) { Start-Sleep -Milliseconds 700; Shot $c[0] (Join-Path $shots "$label-2-downloading.png"); $shotDl = $true } }
            Start-Sleep -Milliseconds 300
        }
        if (-not $p.HasExited) { Fail "$label : the old version did not exit"; return }
        Log ("old exited after {0:0.0}s" -f $sw.Elapsed.TotalSeconds)
        $sw = [Diagnostics.Stopwatch]::StartNew(); $np = $null
        while ($sw.Elapsed.TotalSeconds -lt 30 -and -not $np) {
            Start-Sleep -Milliseconds 400
            $np = Get-CimInstance Win32_Process -Filter "Name='Subtext.exe'" | Where-Object { $_.ExecutablePath -eq $appExe -and $_.ProcessId -ne $p.Id } | Select-Object -First 1
        }
        $same = (Sha $appExe) -eq $newHash
        if (-not $same) { Fail "$label : the replaced file is not the published one" } else { Log "file == published Subtext.exe" }
        if (-not $np) { Fail "$label : the new version did not start" } else {
            Log ("new instance pid=" + $np.ProcessId)
            Start-Sleep -Seconds 4
            foreach ($h in [SW]::Tops([uint32]$np.ProcessId)) { Shot $h (Join-Path $shots "$label-3-after.png"); break }
        }
        $reqs = @(Get-Content $logFile -ErrorAction SilentlyContinue)
        $gotGz = @($reqs | Where-Object { $_ -like '*Subtext-app-update.gz' }).Count -gt 0
        $gotFull = @($reqs | Where-Object { $_ -like '*/Subtext.exe' }).Count -gt 0
        Log ("server: " + ($reqs -join ' | '))
        if ($expectSlim) {
            if (-not $gotGz -or $gotFull) { Fail "$label : expected only the small file (gz=$gotGz full=$gotFull)" }
            $engineNow = if (Test-Path $engine) { (Get-Item $engine).LastWriteTimeUtc } else { $null }
            if ($engineTime -ne $null -and $engineNow -ne $engineTime) { Fail "$label : the engine was deployed again" } else { Log "engine kept (not deployed again)" }
        } else {
            if (-not $gotGz -or -not $gotFull) { Fail "$label : expected the small try and then the full file (gz=$gotGz full=$gotFull)" }
        }
        if ($np) { Stop-Process -Id $np.ProcessId -Force -ErrorAction SilentlyContinue }
    } finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        Set-Content $stopFile 'x'
        Start-Sleep -Milliseconds 300
        if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue }
        Remove-Item Env:\SUBSTUDIO_UPDATE_API -ErrorAction SilentlyContinue
    }
}

# עדכון אוטומטי חייב להיות דלוק; ההגדרות חוזרות בסוף (כמו test-upgrade-real)
$ini = Join-Path $env:APPDATA 'SubtitleStudio\settings.ini'
$iniBak = Join-Path $base 'settings.ini.bak'
if (Test-Path $ini) { Copy-Item $ini $iniBak -Force }
try {
    if (Test-Path $ini) {
        $lines = @(Get-Content $ini -Encoding UTF8 | Where-Object { $_ -notmatch '^autoupdate=' }) + 'autoupdate=1'
        [IO.File]::WriteAllLines($ini, $lines, (New-Object Text.UTF8Encoding $false))
    }
    Scenario 'A-slim' $old $new 120 $false $true
    Scenario 'B-engine-changed' $old $newB 0 $false $false
    Scenario 'C-cancel' $old $new 20 $true $false
} finally {
    if (Test-Path $iniBak) { Copy-Item $iniBak $ini -Force }
}
Log ("shots: " + $shots)
if ($script:failures -gt 0) { Log ("FAILED: " + $script:failures); exit 1 }
Log "SLIM UPDATE OK"
