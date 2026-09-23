# צילום של כל חלון בתוכנה, **מחוץ למסך** - בלי לפתוח שום דבר מול המשתמש ובלי
# לגנוב את המיקוד. בשביל הסבב החזותי: לפני/אחרי, שתי ערכות, שתי שפות.
#
#   shots.ps1                    כל החלונות, ערכה כהה, עברית
#   shots.ps1 -Light -Lang en    ערכה בהירה, אנגלית
#   shots.ps1 -Only Export,Main  רק אלה
#
# הפלט: %TEMP%\ss-shots\<ערכה>-<שפה>\<שם>.png
# **החלון הראשי לא כאן:** מחוץ למסך ובלי Show הוא לא מסדר את עצמו (DoLayout לא
# מספיק). בשבילו test-real-open.ps1 (עם סרט) או test-run.ps1 (מסך הפתיחה).
# ‏dialog-gallery.ps1 הישן מציג כל חלון על המסך (Show) ולוקח את המיקוד. לא להשתמש בו
# כשבנימין ליד המחשב.
param([string[]]$Only = @(), [switch]$Light, [string]$Lang = 'he', [double]$Scale = 1.25)

$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class ShotDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][ShotDpi]::SetProcessDPIAware()

$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }

(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]$Scale)
(TY 'Theme').GetField('Dark', $ST).SetValue($null, (-not $Light))
[void](TY 'Lang').GetMethod('Set', $ST).Invoke($null, @([string]$Lang))
if (-not (TY 'Fonts').GetProperty('Ready', $ST).GetValue($null, $null)) { throw 'the embedded font did not load - is dist\Subtext.exe a build.cmd build?' }

$dest = Join-Path $env:TEMP ('ss-shots\' + $(if ($Light) { 'light' } else { 'dark' }) + '-' + $Lang)
New-Item -ItemType Directory -Force $dest | Out-Null

# ---- חומרים ----
$doc = NewOf 'Doc' @()
$dcues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
foreach ($c in @(@(1000, 4000, 'שלום לכולם, וברוכים הבאים'), @(5500, 9000, 'This is a test line'))) { $dcues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
$style = NewOf 'SubStyle' @()
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $media)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$mi = (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$media))
function ToolNamed($like) {
    $all = (TY 'MediaTools').GetMethod('All', $ST).Invoke($null, @())
    foreach ($x in $all) { if ((TY 'MediaTool').GetField('Name', $IN).GetValue($x) -like $like) { return $x } }
    return $all[0]
}
$rel = [Activator]::CreateInstance((TY 'Updater+Release')); $rel.Version = '0.9.9'; $rel.Notes = 'x'
$sum = (TY 'Changelog').GetMethod('Build', $ST).Invoke($null, @([string][IO.File]::ReadAllText((Join-Path $root 'changelog.json'), [Text.Encoding]::UTF8), [string]'0.1.0', [string]'0.7.0'))

function MainWith([int]$w, [int]$h, [bool]$withMedia)
{
    $m = [Activator]::CreateInstance((TY 'MainForm'))
    $m.Size = New-Object Drawing.Size $w, $h
    if ($withMedia)
    {
        [void](TY 'MainForm').GetMethod('OpenMedia', $IN).Invoke($m, @([string]$media))
        $mdoc = (TY 'MainForm').GetField('_doc', $IN).GetValue($m)
        $mc = (TY 'Doc').GetField('Cues', $IN).GetValue($mdoc)
        foreach ($c in @(@(800, 2600, 'ברוכים הבאים לשיעור'), @(2900, 5200, 'היום נלמד על כתוביות'), @(5600, 8400, 'Subtitles in English, too'))) { $mc.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
    }
    return $m
}

$forms = @(
    @{ n = 'Settings';    make = { NewOf 'SettingsDlg' @((MainWith 1200 800 $false)) } },
    @{ n = 'About';       make = { NewOf 'AboutDlg' @() } },
    @{ n = 'Export';      make = { NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [int64]-1, [int64]-1) } },
    @{ n = 'Tools';       make = { NewOf 'ToolsDlg' @($null, $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'FitSize';     make = { NewOf 'ToolRunDlg' @($null, (ToolNamed '*גודל קובץ*'), $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Trim';        make = { NewOf 'TrimDlg' @($null, $mi, $doc, [int64]5000, [int64]15000) } },
    @{ n = 'Sync';        make = { NewOf 'SyncDlg' @($doc, [int64]7900, (NewOf 'SyncState' @())) } },
    @{ n = 'Shift';       make = { NewOf 'ShiftDlg' @($doc, [int64]5000) } },
    @{ n = 'Style';       make = { NewOf 'StyleDlg' @($style, $null) } },
    @{ n = 'Find';        make = { NewOf 'FindDlg' @([string]'שלום', [int]3) } },
    @{ n = 'Replace';     make = { NewOf 'ReplaceDlg' @($doc) } },
    @{ n = 'ImportText';  make = { NewOf 'ImportTextDlg' @([int64]0, $true) } },
    @{ n = 'Transcribe';  make = { NewOf 'TranscribeDlg' @($mi, [int]3) } },
    @{ n = 'Extract';     make = { NewOf 'ExtractSubsDlg' @($null, $mi) } },
    @{ n = 'Help';        make = { NewOf 'HelpDlg' @() } },
    @{ n = 'AiSetup';     make = { NewOf 'AiSetupDlg' @() } },
    @{ n = 'GroqSetup';   make = { NewOf 'GroqSetupDlg' @() } },
    @{ n = 'SpellSetup';  make = { NewOf 'SpellSetupDlg' @() } },
    @{ n = 'AiTranslate'; make = { NewOf 'AiTranslateDlg' @($doc) } },
    @{ n = 'Update';      make = { NewOf 'UpdateDlg' @($rel, $sum) } },
    @{ n = 'Fps';         make = { NewOf 'FpsDlg' @($doc) } }
)

$createCtl = [Windows.Forms.Control].GetMethod('CreateControl', $IN, $null, [Type[]]@([bool]), $null)
foreach ($f in $forms)
{
    if ($Only.Count -gt 0 -and -not ($Only -contains $f.n)) { continue }
    try
    {
        $form = & $f.make
        $form.StartPosition = 'Manual'
        $form.Location = New-Object Drawing.Point -12000, 0
        [void]$form.Handle
        [void]$createCtl.Invoke($form, @($true))
        $form.PerformLayout()
        # החלון הראשי מסדר את עצמו ב-DoLayout, שנקרא רק כשהחלון מוצג. Show היה
        # לוקח את המיקוד גם מחוץ למסך, אז קוראים לו ישירות.
        $dl = $form.GetType().GetMethod('DoLayout', $IN)
        if ($dl -ne $null) { [void]$dl.Invoke($form, @()) }
        # פס הקול והפריים מגיעים ברקע
        $until = [DateTime]::Now.AddMilliseconds($(if ($f.n -like 'Main*') { 2500 } else { 150 }))
        while ([DateTime]::Now -lt $until) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
        $cs = $form.ClientSize
        $bmp = New-Object Drawing.Bitmap $cs.Width, $cs.Height
        # רק אזור הלקוח: המסגרת ש-DrawToBitmap מצייר היא הקלאסית, לא הכהה של ווינדוס 11
        $full = New-Object Drawing.Bitmap $form.Width, $form.Height
        $form.DrawToBitmap($full, (New-Object Drawing.Rectangle 0, 0, $form.Width, $form.Height))
        $pt = $form.PointToScreen((New-Object Drawing.Point 0, 0))
        $ox = $pt.X - $form.Left; $oy = $pt.Y - $form.Top
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.DrawImage($full, (New-Object Drawing.Rectangle 0, 0, $cs.Width, $cs.Height), (New-Object Drawing.Rectangle $ox, $oy, $cs.Width, $cs.Height), [Drawing.GraphicsUnit]::Pixel)
        $g.Dispose(); $full.Dispose()
        $bmp.Save((Join-Path $dest ($f.n + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host ('  {0,-12} {1}x{2}' -f $f.n, $cs.Width, $cs.Height)
        $form.Dispose()
    }
    catch
    {
        $m = $_.Exception.Message
        if ($_.Exception.InnerException) { $m = $_.Exception.InnerException.Message }
        Write-Host ('  {0,-12} FAILED: {1}' -f $f.n, $m) -ForegroundColor Red
    }
}
Write-Host ('-> ' + $dest)
