# Packs one download of the model catalog (src\LocalCatalog.cs, WORKPLAN 2lamed-dalet): one .gz per file,
# and optionally a whole folder as ONE zip (an engine of many files in folders, e.g. the embedded Python).
# The .gz files go to a GitHub pre-release (the pack's Tag); the app downloads them one by one, checks the
# SHA-256 of the unpacked file, and unpacks a zip into the pack folder (LocalModel.DownloadFiles).
#
#   build\make-model-pack.ps1 -Out <folder> [-Files <file>,<file>...] [-ZipName <name-N.zip> -ZipDir <folder>]
#
# A zip's name carries its version (engine-ct2-1.zip): the app marks an unpacked zip with "<name>.ok", so a
# new version of the same engine needs a new name. A file bigger than 1900 MiB goes as raw parts (<name>.part1,
# part2... of 1900 MiB, not gz; LocalModel.SplitAt), and its table row has the same size for both columns.
#
# Prints the table for the ModelPack (names, sizes, packed sizes, SHA-256 of the UNPACKED file).
# ASCII only on purpose: PowerShell 5.1 reads a file without a BOM as the ANSI code page.
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$Files = @(),
    [string]$ZipName = '',
    [string]$ZipDir = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force $Out | Out-Null

function Hex([byte[]]$h) { return (-join ($h | ForEach-Object { $_.ToString('X2') })) }

# gz of one file, streamed (a model is hundreds of MB); returns the SHA-256 of the source
function GzFile([string]$src, [string]$dst) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $in = [IO.File]::OpenRead($src)
    $fs = [IO.File]::Create($dst)
    $z = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
    $buf = New-Object byte[] (1MB)
    while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) { $z.Write($buf, 0, $n); [void]$sha.TransformBlock($buf, 0, $n, $null, 0) }
    [void]$sha.TransformFinalBlock($buf, 0, 0)
    $z.Dispose(); $fs.Dispose(); $in.Dispose()
    return (Hex $sha.Hash)
}

$items = New-Object System.Collections.ArrayList
foreach ($f in $Files) { [void]$items.Add(@{ n = (Split-Path $f -Leaf); p = (Resolve-Path $f).Path }) }
$tmpZip = $null
if ($ZipName) {
    if (-not $ZipName.EndsWith('.zip')) { throw 'ZipName must end with .zip' }
    # stored, not deflated: the .gz around it compresses the whole stream anyway
    $tmpZip = Join-Path $Out $ZipName
    if (Test-Path $tmpZip) { Remove-Item $tmpZip }
    $root = (Resolve-Path $ZipDir).Path.TrimEnd('\') + '\'
    $za = [IO.Compression.ZipFile]::Open($tmpZip, [IO.Compression.ZipArchiveMode]::Create)
    foreach ($fi in (Get-ChildItem $root -Recurse -File | Sort-Object FullName)) {
        $rel = $fi.FullName.Substring($root.Length).Replace('\', '/')
        if ($rel -match '(^|/)__pycache__/') { continue }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $fi.FullName, $rel, [IO.Compression.CompressionLevel]::NoCompression)
    }
    $za.Dispose()
    [void]$items.Add(@{ n = $ZipName; p = $tmpZip })
}

$names = @(); $sizes = @(); $packed = @(); $hashes = @()
foreach ($it in $items) {
    $len = (Get-Item $it.p).Length
    if ($len -gt 1900MB) {
        # raw parts of 1900 MiB (LocalModel.SplitAt) - a GitHub release file is limited to 2 GB; model weights hardly compress
        $sha = [Security.Cryptography.SHA256]::Create()
        $in = [IO.File]::OpenRead($it.p); $buf = New-Object byte[] (4MB); $k = 0; $left = $len
        while ($left -gt 0) {
            $k++; $part = [IO.File]::Create((Join-Path $Out ($it.n + '.part' + $k))); $want = [Math]::Min([int64]1900MB, $left)
            while ($want -gt 0) { $n = $in.Read($buf, 0, [int][Math]::Min($buf.Length, $want)); $part.Write($buf, 0, $n); [void]$sha.TransformBlock($buf, 0, $n, $null, 0); $want -= $n; $left -= $n }
            $part.Dispose()
        }
        [void]$sha.TransformFinalBlock($buf, 0, 0); $in.Dispose()
        $h = Hex $sha.Hash
        $names += $it.n; $sizes += $len; $packed += $len; $hashes += $h
        '{0,-28} {1,11} -> {2} raw parts  {3}' -f $it.n, $len, $k, $h
        continue
    }
    $gz = Join-Path $Out ($it.n + '.gz')
    $h = GzFile $it.p $gz
    $g = (Get-Item $gz).Length
    $names += $it.n; $sizes += $len; $packed += $g; $hashes += $h
    '{0,-28} {1,11} -> {2,11}  {3}' -f $it.n, $len, $g, $h
}
if ($tmpZip) { Remove-Item $tmpZip }
''
'p.Files = new string[] { ' + (($names | ForEach-Object { '"' + $_ + '"' }) -join ', ') + ' };'
'p.Sizes = new long[] { ' + ($sizes -join ', ') + ' };'
'p.GzSizes = new long[] { ' + ($packed -join ', ') + ' };'
'p.Sha256 = new string[] {'
$hashes | ForEach-Object { '    "' + $_ + '",' }
'};'
'download: {0} bytes ({1:N1} MB), on disk: {2:N1} MB' -f (($packed | Measure-Object -Sum).Sum), ((($packed | Measure-Object -Sum).Sum) / 1MB), ((($sizes | Measure-Object -Sum).Sum) / 1MB)
