# שחזור של תמלול קטע אחד בדיוק כמו התוכנה, ושמירת התשובה **הגולמית** של השרת.
# בשביל באגים שבהם ״התמלול פספס חלק״: מה השרת החזיר, ומה התוכנה עשתה עם זה.
#   tr-probe.ps1 -Media <קובץ> -Starts 55,110 [-Out <תיקייה>]
# משתמש במפתח ובדגם מההגדרות של המשתמש. כל קטע = בקשה אחת מהמכסה.
param([Parameter(Mandatory = $true)][string]$Media, [string]$Starts = '0', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
if ($Out -eq '') { $Out = Join-Path $env:TEMP 'ss-trprobe' }
New-Item -ItemType Directory -Force $Out | Out-Null

[void](TY 'Settings').GetMethod('Load', $ST).Invoke($null, @())
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
if (-not (TY 'Ai').GetProperty('HasKey', $ST).GetValue($null, $null)) { throw 'no Google key in settings' }
$ffExe = [string](TY 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
$chunk = (TY 'Transcribe').GetField('ChunkSec', $ST).GetValue($null)
$trT = TY 'Ai'
$tc = $trT.GetMethod('TranscribeChunk', $ST)

foreach ($sStr in ($Starts -split ',')) {
    $s = [double]$sStr
    $mp3 = Join-Path $Out ('chunk_' + [int]$s + '.mp3')
    $args = '-y -hide_banner -v error -ss ' + $s + ' -t ' + $chunk + ' -i "' + $Media + '" -vn -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k "' + $mp3 + '"'
    $a = New-Object object[] 5; $a[0] = $ffExe; $a[1] = $args; $a[4] = $Out
    [void](TY 'Ff').GetMethod('RunSync', $ST).Invoke($null, $a)
    $bytes = [IO.File]::ReadAllBytes($mp3)

    # הכלי רושם את התשובה הגולמית דרך LastRaw (ראו Ai.cs); אם אין - רק השורות
    $argv = New-Object object[] 4; $argv[0] = $bytes; $argv[1] = 'audio/mp3'; $argv[2] = ''
    $lines = $tc.Invoke($null, $argv)
    $raw = ''
    $rawF = $trT.GetField('LastRaw', $ST)
    if ($rawF -ne $null) { $raw = [string]$rawF.GetValue($null) }
    [IO.File]::WriteAllText((Join-Path $Out ('raw_' + [int]$s + '.txt')), $raw, (New-Object Text.UTF8Encoding $true))
    Write-Host ('--- chunk at ' + $s + 's: ' + $bytes.Length + ' bytes, error=' + $argv[3])
    if ($lines -ne $null) {
        Write-Host ('    parsed lines: ' + $lines.Count)
        foreach ($ln in $lines) { Write-Host ('    {0,6:0.00} {1,6:0.00}  {2}' -f $ln.Start, $ln.End, $ln.Text) }
    }
    Write-Host ('    raw (' + $raw.Length + ' chars): ' + $(if ($raw.Length -gt 600) { $raw.Substring(0, 600) + ' ...' } else { $raw }))
}
