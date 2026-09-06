# יוצר את קובצי הבדיקה ש-build\test-*.ps1 מסתמכים עליהם.
# רץ פעם אחת; אם הקבצים קיימים הוא לא נוגע בהם.
#
#   powershell -ExecutionPolicy Bypass -File build\make-testmedia.ps1
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:TEMP 'ss-gallery'
New-Item -ItemType Directory -Force $dest | Out-Null

$ff = Join-Path $env:LOCALAPPDATA 'SubtitleStudio\runtime\ffmpeg.exe'
if (-not (Test-Path $ff)) { $ff = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\ffmpeg.exe' }
if (-not (Test-Path $ff)) {
    Write-Host 'ffmpeg not found - run the app once so it unpacks its engine, or put ffmpeg.exe in tools\'
    exit 1
}

function Make($name, $ffargs) {
    $path = Join-Path $dest $name
    if (Test-Path $path) { Write-Host ("  ok   $name (already there)"); return }
    & $ff -y @ffargs $path 2>$null | Out-Null
    if (Test-Path $path) { Write-Host ("  made $name") } else { Write-Host ("  FAIL $name") }
}

# סרט רגיל עם קול - הבסיס לרוב הבדיקות
Make 'test.mp4' @(
    '-f', 'lavfi', '-i', 'testsrc=size=640x360:rate=25:duration=40',
    '-f', 'lavfi', '-i', 'sine=frequency=440:duration=40',
    '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-shortest')

# סרט בלי פס קול - שם היה הבאג של הציר שמתרענן בלי סוף
Make 'silent.mp4' @(
    '-f', 'lavfi', '-i', 'testsrc=size=320x180:rate=15:duration=6',
    '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-an')

Write-Host ''
Write-Host "media: $dest"
