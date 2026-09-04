# בדיקת מהירות ניגון בלי ממשק: מפעילים את המנוע ומודדים כמה זמן בסרט עבר בזמן אמיתי קבוע.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\SubtitleStudio.exe'
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $exe))   { Write-Host 'no exe - run build.cmd first'; exit 1 }
if (-not (Test-Path $media)) { Write-Host "no media: $media"; exit 1 }

$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exe))
$T = { param($n) $asm.GetType("SubtitleStudio.$n") }
function Pack {
    $a = New-Object object[] $args.Count
    for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }
    return ,$a
}

$ff = & $T 'Ff'
$ffExe = $ff.GetProperty('Exe').GetValue($null)
if (-not $ffExe -or -not (Test-Path $ffExe)) { Write-Host "ffmpeg not deployed yet - run the app once"; exit 1 }
Write-Host ("ffmpeg: " + $ffExe)

$mi = $ff.GetMethod('ProbeFile').Invoke($null, (Pack ([string]$media)))
$engT = & $T 'Engine'
$pass = 0; $fail = 0

function Measure-Speed($sp) {
    $e = [Activator]::CreateInstance($engT)
    $engT.GetMethod('Open').Invoke($e, (Pack ([string]$media) $mi)) | Out-Null
    $engT.GetProperty('Speed').SetValue($e, [double]$sp)
    $engT.GetMethod('Play').Invoke($e, (Pack)) | Out-Null
    Start-Sleep -Milliseconds 600          # התייצבות של waveOut
    $p0 = $engT.GetProperty('Position').GetValue($e)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds 3000
    $wall = $sw.ElapsedMilliseconds
    $p1 = $engT.GetProperty('Position').GetValue($e)
    $engT.GetMethod('Pause').Invoke($e, (Pack)) | Out-Null
    $engT.GetMethod('Close').Invoke($e, (Pack)) | Out-Null
    if ($wall -le 0) { return 0 }
    return [math]::Round(($p1 - $p0) / [double]$wall, 3)
}

foreach ($sp in @(1.0, 0.5, 1.5)) {
    $got = Measure-Speed $sp
    $ok = [math]::Abs($got - $sp) -lt 0.12
    if ($ok) { $pass++; Write-Host ("  ok   x{0}  -> {1} " -f $sp, $got) }
    else { $fail++; Write-Host ("  FAIL x{0}  -> {1}" -f $sp, $got) -ForegroundColor Red }
}

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
