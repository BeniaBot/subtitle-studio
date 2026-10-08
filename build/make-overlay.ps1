# Builds the two release files from the compiled app (build\payload\Subtext-app.exe, compiled
# WITHOUT the engine) and the engine pack:
#
#   dist\Subtext.exe             = app + ffmpeg.pack + 64-byte trailer   (what users download)
#   dist\Subtext-app-update.gz   = the app part alone, gzipped           (the small update, 0.8.8)
#
# The trailer, at the very end of the file:
#   16 bytes  "SUBTEXT-ENGINE-1"
#    8 bytes  length of the pack (Int64, little endian)
#   32 bytes  SHA-256 of the pack - the engine's identity
#    8 bytes  zero (reserved)
#
# Nothing in the trailer depends on the app part, so engine + trailer are the same bytes in every
# version built with the same engine - the updater copies them from the old EXE as they are. The
# engine starts at (file length - 64 - pack length).
#
# Windows and .NET ignore data after the last PE section, so the EXE runs as before (tested on
# 7.10.2026: a normal start, and Assembly.Load of the bytes in the test scripts). The updater of
# 0.8.8+ downloads the gz, appends the engine bytes it already has, and checks the SHA-256 of the
# result against the published Subtext.exe - anything else, and it downloads the whole file.
# ASCII only on purpose (no BOM trap).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'build\payload\Subtext-app.exe'
$pack = Join-Path $root 'build\payload\ffmpeg.pack'
$out = Join-Path $root 'dist\Subtext.exe'
$gz = Join-Path $root 'dist\Subtext-app-update.gz'

if (-not (Test-Path $app)) { Write-Host "[ERROR] $app is missing - compile first."; exit 1 }
$appBytes = [IO.File]::ReadAllBytes($app)

$fs = [IO.File]::Create($out)
try {
    $fs.Write($appBytes, 0, $appBytes.Length)
    if (Test-Path $pack) {
        $sha = [Security.Cryptography.SHA256]::Create()
        $packLen = (Get-Item $pack).Length
        $in = [IO.File]::OpenRead($pack)
        try {
            $buf = New-Object byte[] (1MB)
            while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
                $fs.Write($buf, 0, $n)
                [void]$sha.TransformBlock($buf, 0, $n, $null, 0)
            }
            [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        } finally { $in.Dispose() }
        $bw = New-Object IO.BinaryWriter($fs, [Text.Encoding]::ASCII, $true)
        $bw.Write([byte[]][char[]]'SUBTEXT-ENGINE-1')
        $bw.Write([Int64]$packLen)
        $bw.Write([byte[]]$sha.Hash)
        $bw.Write([Int64]0)
        $bw.Flush(); $bw.Dispose()
        $id = ([BitConverter]::ToString($sha.Hash) -replace '-', '').ToLower().Substring(0, 12)
        Write-Host ("[ok] engine attached ({0:N1} MB, id {1})" -f ($packLen / 1MB), $id)
    } else {
        Write-Host "[skip] no engine pack - dist\Subtext.exe is the app alone."
    }
} finally { $fs.Dispose() }

$gfs = [IO.File]::Create($gz)
try {
    $gzs = New-Object IO.Compression.GZipStream($gfs, [IO.Compression.CompressionLevel]::Optimal, $true)
    $gzs.Write($appBytes, 0, $appBytes.Length)
    $gzs.Dispose()
} finally { $gfs.Dispose() }
Write-Host ("[ok] app part {0:N2} MB, update file {1:N2} MB" -f ($appBytes.Length / 1MB), ((Get-Item $gz).Length / 1MB))
