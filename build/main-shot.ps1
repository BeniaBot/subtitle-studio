# main-shot.ps1 - החלון הראשי ומסך הפתיחה, **מחוץ למסך** (כמו test-layout), לתמונה.
# ‏shots.ps1 לא מצלם אותם: בלי Show החלון לא מסדר את עצמו. כאן יש Show, אבל
# במיקום ‎-3000,-3000 - שום דבר לא נפתח מול המשתמש.
#   main-shot.ps1 [-Lang en] [-Light] [-W 1493 -H 997]
# הפלט: %TEMP%\ss-shots\main-<ערכה>-<שפה>.png ו-hero-<ערכה>-<שפה>.png
param([string]$Lang = 'he', [switch]$Light, [int]$W = 1493, [int]$H = 997, [double]$Scale = 1.25)
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class MainShotDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][MainShotDpi]::SetProcessDPIAware()
[System.Windows.Forms.Application]::EnableVisualStyles()

$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }

(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]$Scale)
(TY 'Theme').GetField('Dark', $ST).SetValue($null, (-not $Light))
[void](TY 'Lang').GetMethod('Set', $ST).Invoke($null, @([string]$Lang))
if (-not (TY 'Fonts').GetProperty('Ready', $ST).GetValue($null, $null)) { throw 'the embedded font did not load' }
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())

$dest = Join-Path $env:TEMP 'ss-shots'
New-Item -ItemType Directory -Force $dest | Out-Null
$tag = $(if ($Light) { 'light' } else { 'dark' }) + '-' + $Lang
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $media)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }

function Pump([int]$ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }

function Snap($f, $file) {
    $bmp = New-Object Drawing.Bitmap $f.ClientSize.Width, $f.ClientSize.Height
    # טקסט שנחתך ב״...״ (Theme.ClipLog). ברשימה ובבלוקים של הציר זה בכוונה - לקרוא, לא להיכשל
    $log = New-Object 'System.Collections.Generic.List[string]'
    (TY 'Theme').GetField('ClipLog', $ST).SetValue($null, $log)
    $f.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $f.ClientSize.Width, $f.ClientSize.Height))
    (TY 'Theme').GetField('ClipLog', $ST).SetValue($null, $null)
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host ('  ' + (Split-Path $file -Leaf))
    foreach ($x in $log) { Write-Host ('      ... ' + $x) -ForegroundColor Yellow }
}

function NewMain {
    $m = [Activator]::CreateInstance((TY 'MainForm'))
    $m.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $m.Location = New-Object Drawing.Point -3000, -3000
    $m.ShowInTaskbar = $false
    $m.Show()
    $m.ClientSize = New-Object Drawing.Size $W, $H
    Pump 400
    return $m
}

# ---- מסך הפתיחה ----
$m = NewMain
Snap $m (Join-Path $dest ("hero-$tag.png"))
$m.Close(); $m.Dispose()

# ---- מסך העבודה ----
$m = NewMain
[void](TY 'MainForm').GetMethod('OpenMedia', $IN).Invoke($m, @([string]$media))
$doc = (TY 'MainForm').GetField('_doc', $IN).GetValue($m)
$cues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
foreach ($c in @(@(800, 2600, 'ברוכים הבאים לשיעור'), @(2900, 5200, 'היום נלמד על כתוביות'), @(5600, 8400, 'Subtitles in English, too'))) { $cues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
$fs = (TY 'Cue').GetField('Selected', $IN); if ($fs) { $fs.SetValue($cues[1], $true) } else { (TY 'Cue').GetProperty('Selected', $IN).SetValue($cues[1], $true, $null) }
foreach ($n in 'RefreshAll', 'LoadEditor') { $mm = (TY 'MainForm').GetMethod($n, $IN); if ($mm) { [void]$mm.Invoke($m, @()) } }
Pump 900
Snap $m (Join-Path $dest ("main-$tag.png"))
$m.Close(); $m.Dispose()
