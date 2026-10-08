# Packs tools\ffmpeg.exe into build\payload\ffmpeg.pack.
# make-overlay.ps1 appends the pack to the end of dist\Subtext.exe; the app unpacks it on first run.
#
# Two formats, told apart by the magic (src\Runtime.cs reads both):
#   FFP2 + Int64 size + an LZMA stream (".lzma": 5 bytes props + 8 bytes size + data)  - 27 MB for 9.0.2
#   FFP1 + Int64 size + a Deflate stream                                              - 37 MB
# LZMA needs Python's lzma module to pack (only when ffmpeg.exe changes, about a minute); without
# Python the build still works, with the bigger Deflate pack. The app decodes LZMA itself (src\Lzma.cs).
#
# The pack is rebuilt only when tools\ffmpeg.exe really changed - by its SHA-256, recorded in
# ffmpeg.pack.src. Until 0.8.8 this compared file dates, and an ffmpeg.exe taken out of a zip keeps
# the date it was built on: a newer engine with an older date would have kept the old pack.
#
# Keep the pack: it is the engine's identity. App-only updates (0.8.8) rebuild the user's new EXE
# from the small app file plus the engine bytes they already have, and check the result against
# the published Subtext.exe - repacking the same ffmpeg.exe can give other bytes (another
# compressor version), and then every user downloads the whole file once.
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'tools\ffmpeg.exe'
$outDir = Join-Path $root 'build\payload'
$out = Join-Path $outDir 'ffmpeg.pack'
$stamp = Join-Path $outDir 'ffmpeg.pack.src'

if (-not (Test-Path $src)) {
    Write-Host "[skip] tools\ffmpeg.exe not found - building without an embedded engine."
    exit 0
}
New-Item -ItemType Directory -Force $outDir | Out-Null

$srcInfo = Get-Item $src
$srcHash = (Get-FileHash $src -Algorithm SHA256).Hash
if ((Test-Path $out) -and (Test-Path $stamp)) {
    $was = (Get-Content $stamp -TotalCount 1).Trim()
    if ($was -eq $srcHash) {
        Write-Host ("[ok] payload up to date ({0:N1} MB)" -f ((Get-Item $out).Length / 1MB))
        exit 0
    }
}

Write-Host ("packing {0:N1} MB ..." -f ($srcInfo.Length / 1MB))
$sw = [Diagnostics.Stopwatch]::StartNew()
$py = Get-Command python -ErrorAction SilentlyContinue
$lz = Join-Path $outDir 'ffmpeg.lzma.tmp'
$useLzma = $false
if ($py) {
    & $py.Source -c "import lzma,sys; d=open(sys.argv[1],'rb').read(); open(sys.argv[2],'wb').write(lzma.compress(d, format=lzma.FORMAT_ALONE, preset=9|lzma.PRESET_EXTREME))" $src $lz
    $useLzma = ($LASTEXITCODE -eq 0) -and (Test-Path $lz) -and ((Get-Item $lz).Length -gt 1000000)
    if (-not $useLzma) { Write-Host "[warn] python could not make the LZMA stream - packing with Deflate" }
} else { Write-Host "[warn] python not found - packing with Deflate (about 10 MB bigger)" }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([byte[]][char[]]$(if ($useLzma) { 'FFP2' } else { 'FFP1' }))   # magic
$bw.Write([Int64]$srcInfo.Length)          # original size
$bw.Flush()
if ($useLzma) {
    $in = [System.IO.File]::OpenRead($lz)
    $in.CopyTo($fs, 1MB)
    $in.Dispose(); $bw.Dispose(); $fs.Dispose()
    [System.IO.File]::Delete($lz)
} else {
    $in = [System.IO.File]::OpenRead($src)
    $ds = New-Object System.IO.Compression.DeflateStream($fs, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $in.CopyTo($ds, 1MB)
    $ds.Dispose(); $bw.Dispose(); $fs.Dispose(); $in.Dispose()
}
Set-Content -Path $stamp -Value $srcHash -Encoding ASCII
$sw.Stop()
Write-Host ("[ok] {0} -> {1:N1} MB ({2}) in {3:N0}s" -f 'ffmpeg.pack', ((Get-Item $out).Length / 1MB), $(if ($useLzma) { 'LZMA' } else { 'Deflate' }), $sw.Elapsed.TotalSeconds)
