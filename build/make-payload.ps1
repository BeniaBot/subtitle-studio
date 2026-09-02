# Packs tools\ffmpeg.exe into build\payload\ffmpeg.pack (header + deflate stream).
# The pack is embedded into the EXE as a resource and unpacked on first run.
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'tools\ffmpeg.exe'
$outDir = Join-Path $root 'build\payload'
$out = Join-Path $outDir 'ffmpeg.pack'

if (-not (Test-Path $src)) {
    Write-Host "[skip] tools\ffmpeg.exe not found - building without an embedded engine."
    exit 0
}
New-Item -ItemType Directory -Force $outDir | Out-Null

$srcInfo = Get-Item $src
if (Test-Path $out) {
    $packInfo = Get-Item $out
    if ($packInfo.LastWriteTime -gt $srcInfo.LastWriteTime) {
        Write-Host ("[ok] payload up to date ({0:N1} MB)" -f ($packInfo.Length / 1MB))
        exit 0
    }
}

Write-Host ("packing {0:N1} MB ..." -f ($srcInfo.Length / 1MB))
$sw = [Diagnostics.Stopwatch]::StartNew()
$in = [System.IO.File]::OpenRead($src)
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([byte[]][char[]]'FFP1')          # magic
$bw.Write([Int64]$srcInfo.Length)          # original size
$bw.Flush()
$ds = New-Object System.IO.Compression.DeflateStream($fs, [System.IO.Compression.CompressionLevel]::Optimal, $true)
$in.CopyTo($ds, 1MB)
$ds.Dispose(); $bw.Dispose(); $fs.Dispose(); $in.Dispose()
$sw.Stop()
Write-Host ("[ok] {0} -> {1:N1} MB in {2:N0}s" -f 'ffmpeg.pack', ((Get-Item $out).Length / 1MB), $sw.Elapsed.TotalSeconds)
