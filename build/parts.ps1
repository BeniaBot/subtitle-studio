# רכיבי מסך העבודה, כל אחד לבד ומחוץ למסך: רשימת הכתוביות וציר הזמן.
#
# את מסך העבודה המלא אפשר לצלם רק בהרצה אמיתית (test-real-open.ps1), ושם אין
# שורה נבחרת ואין ריחוף. כאן הרכיב נבנה עם מצבים ידועים - שורה נבחרת, שורה
# בריחוף, שורה שמתנגנת, בלוק נבחר - כדי לשפוט את הציור שלהם.
#
#   parts.ps1 [-Light] [-Lang en]      הפלט: %TEMP%\ss-shots\parts-<ערכה>-<שפה>.png
param([switch]$Light, [string]$Lang = 'he', [double]$Scale = 1.25)
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class PartsDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][PartsDpi]::SetProcessDPIAware()
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

$doc = NewOf 'Doc' @()
$cues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
$lines = @(
    @(800, 2600, 'ברוכים הבאים לשיעור'),
    @(2900, 5200, 'היום נלמד על כתוביות'),
    @(5600, 8400, 'Subtitles in English, too'),
    @(8800, 9400, 'שורה מהירה מדי לקריאה, עם הרבה מאוד מילים'),
    @(9900, 12600, 'ועוד שורה רגילה')
)
foreach ($c in $lines) { $cues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
$cues[1].Selected = $true

$createCtl = [Windows.Forms.Control].GetMethod('CreateControl', $IN, $null, [Type[]]@([bool]), $null)
function Render($ctl, $w, $h)
{
    $ctl.Size = New-Object Drawing.Size $w, $h
    [void]$ctl.Handle
    [void]$createCtl.Invoke($ctl, @($true))
    $bmp = New-Object Drawing.Bitmap $w, $h
    $ctl.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $w, $h))
    return $bmp
}

# רשימה: שורה 2 נבחרת, שורה 4 בריחוף, שורה 1 מתנגנת
$list = [Activator]::CreateInstance((TY 'CueList'))
$list.Doc = $doc
$list.Position = 1500
(TY 'CueList').GetField('_hoverRow', $IN).SetValue($list, 3)
$lb = Render $list 560 330

# ציר: בלוק 2 נבחר, בלוק 3 בריחוף
$tl = [Activator]::CreateInstance((TY 'TimelineControl'))
$tl.Doc = $doc
$tl.DurationMs = 14000
$tl.Position = 4200
(TY 'TimelineControl').GetField('_hoverCue', $IN).SetValue($tl, $cues[2])
$tb = Render $tl 900 200

$out = New-Object Drawing.Bitmap 920, 560
$g = [Drawing.Graphics]::FromImage($out)
$bg = (TY 'Theme').GetProperty('Bg', $ST).GetValue($null, $null)
$g.Clear($bg)
$g.DrawImage($lb, 10, 10)
$g.DrawImage($tb, 10, 350)
$g.Dispose(); $lb.Dispose(); $tb.Dispose()
$dir = Join-Path $env:TEMP 'ss-shots'
New-Item -ItemType Directory -Force $dir | Out-Null
$file = Join-Path $dir ('parts-' + $(if ($Light) { 'light' } else { 'dark' }) + '-' + $Lang + '.png')
$out.Save($file, [Drawing.Imaging.ImageFormat]::Png); $out.Dispose()
Write-Host ('-> ' + $file)
