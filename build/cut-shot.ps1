# cut-shot.ps1 - חלון החיתוך עם קובץ אמיתי, מחוץ למסך: התוכנה פותחת את הקובץ, פס הקול נבנה,
# מסמנים קטעים (לשמור + להסיר בתוכו + עוד אחד) ומצלמים. בשביל הבדיקה בעין - לא חבילה.
#   cut-shot.ps1 -Media f.mp3 [-Light] [-Lang en] [-Out dir]
param([string]$Media = '', [switch]$Light, [string]$Lang = 'he', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class CutDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][CutDpi]::SetProcessDPIAware()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]1.25)
(TY 'Theme').GetField('Dark', $ST).SetValue($null, (-not $Light))
[void](TY 'Lang').GetMethod('Set', $ST).Invoke($null, @([string]$Lang))
if ($Out -eq '') { $Out = Join-Path $env:TEMP 'ss-cut-shots' }
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Media -eq '') { $Media = Join-Path $env:TEMP 'ss-gallery\test.mp4' }
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())

$m = [Activator]::CreateInstance((TY 'MainForm'))
$m.StartPosition = 'Manual'; $m.Location = New-Object Drawing.Point -4000, -4000
$m.Size = New-Object Drawing.Size 1500, 1000
$m.Show(); [Windows.Forms.Application]::DoEvents()
[void](TY 'MainForm').GetMethod('OpenMedia', $IN).Invoke($m, @([string]$Media))
$wave = (TY 'MainForm').GetField('_wave', $IN).GetValue($m)
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $wave.Ready -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 100; [Windows.Forms.Application]::DoEvents() }
$mi = (TY 'MainForm').GetField('_mi', $IN).GetValue($m)
$doc = (TY 'MainForm').GetField('_doc', $IN).GetValue($m)
$dur = [long]$mi.DurationMs

$secT = TY 'CutSection'
$secs = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($secT))
$dlg = [Activator]::CreateInstance((TY 'CutDlg'), @($m, $mi, $doc, $wave, $secs))
$dlg.StartPosition = 'Manual'; $dlg.Location = New-Object Drawing.Point -4000, -3000
$dlg.Show(); [Windows.Forms.Application]::DoEvents()
# קטעים: לשמור 10%-35%, להסיר בתוכו 18%-22%, ועוד קטע לשמירה 55%-70%
$add = (TY 'CutDlg').GetMethod('AddSection', $IN)
$secs.Clear()
[void]$add.Invoke($dlg, @([long]($dur * 0.10), [long]($dur * 0.35), $true))
[void]$add.Invoke($dlg, @([long]($dur * 0.18), [long]($dur * 0.22), $false))
[void]$add.Invoke($dlg, @([long]($dur * 0.55), [long]($dur * 0.70), $true))
$view = (TY 'CutDlg').GetField('_view', $IN).GetValue($dlg)
$view.FitAll()
[void](TY 'MainForm').GetMethod('SeekPlayer', $IN).Invoke($m, @([long]($dur * 0.25)))
for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 50; [Windows.Forms.Application]::DoEvents() }
$bmp = New-Object Drawing.Bitmap $dlg.Width, $dlg.Height
$dlg.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $dlg.Width, $dlg.Height))
$name = [IO.Path]::GetFileNameWithoutExtension($Media) + '-' + $(if ($Light) { 'light' } else { 'dark' }) + '-' + $Lang + '.png'
$bmp.Save((Join-Path $Out $name)); $bmp.Dispose()
Write-Host ('shot: ' + (Join-Path $Out $name) + '  size ' + $dlg.Width + 'x' + $dlg.Height + '  wave ready=' + $wave.Ready)
$dlg.Close(); $m.Close()
