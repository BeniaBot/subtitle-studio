# Packs the local transcription download (WORKPLAN 2lamed-dalet): the sherpa-onnx engine + the Hebrew
# zipformer model, one .gz per file, plus the license texts. The files go to a GitHub pre-release
# (tag = LocalModel.PackVersion) and the app downloads them one by one (src\LocalStt.cs).
#
#   build\make-local-stt.ps1 -Engine <folder with sherpa-onnx.exe and onnxruntime.dll>
#                            -Model  <folder with tokens.txt and the three int8 .onnx files>
#                            -Licenses <folder with LICENSE-sherpa-onnx.txt and LICENSE-onnxruntime.txt>
#                            -Out <folder>
#
# Prints the table that goes into LocalModel (names, sizes, packed sizes, SHA-256 of the UNPACKED file).
# ASCII only on purpose: PowerShell 5.1 reads a file without a BOM as the ANSI code page.
param(
    [Parameter(Mandatory = $true)][string]$Engine,
    [Parameter(Mandatory = $true)][string]$Model,
    [Parameter(Mandatory = $true)][string]$Licenses,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null

# One text file with everything the licenses ask us to pass on.
$lic = Join-Path $Out 'LICENSES.txt'
$nl = "`r`n"
$text = "Subtext - local transcription pack" + $nl + $nl +
    "This folder holds third-party software and a speech model. Subtext only runs them; none of it is ours." + $nl + $nl +
    "1. sherpa-onnx 2.0.1 (sherpa-onnx.exe) - Apache License 2.0" + $nl +
    "   https://github.com/k2-fsa/sherpa-onnx" + $nl + $nl +
    "2. ONNX Runtime 1.28.2 (onnxruntime.dll) - MIT License, Copyright (c) Microsoft Corporation" + $nl +
    "   https://github.com/microsoft/onnxruntime  (third-party notices: ThirdPartyNotices.txt in that repository)" + $nl + $nl +
    "3. zipformer2-66m-streaming-he (tokens.txt, encoder.int8.onnx, decoder.int8.onnx, joiner.int8.onnx)" + $nl +
    "   Hebrew speech recognition model developed by Yakov Kolani, trained on ivrit.ai audio." + $nl +
    "   Creative Commons Attribution 4.0 International (CC BY 4.0) - https://creativecommons.org/licenses/by/4.0/" + $nl +
    "   https://huggingface.co/thewh1teagle/zipformer2-66m-streaming-he" + $nl +
    "   https://huggingface.co/ivrit-ai/zipformer2-66m-streaming-he" + $nl +
    "   The files are unchanged." + $nl + $nl +
    "==================== sherpa-onnx: Apache License 2.0 ====================" + $nl + $nl +
    ([IO.File]::ReadAllText((Join-Path $Licenses 'LICENSE-sherpa-onnx.txt')) -replace "`r?`n", $nl) + $nl + $nl +
    "==================== ONNX Runtime: MIT License ====================" + $nl + $nl +
    ([IO.File]::ReadAllText((Join-Path $Licenses 'LICENSE-onnxruntime.txt')) -replace "`r?`n", $nl) + $nl
[IO.File]::WriteAllText($lic, $text, (New-Object Text.UTF8Encoding($false)))

$items = @(
    @{ n = 'sherpa-onnx.exe';   p = (Join-Path $Engine 'sherpa-onnx.exe') },
    @{ n = 'onnxruntime.dll';   p = (Join-Path $Engine 'onnxruntime.dll') },
    @{ n = 'tokens.txt';        p = (Join-Path $Model 'tokens.txt') },
    @{ n = 'encoder.int8.onnx'; p = (Join-Path $Model 'encoder.int8.onnx') },
    @{ n = 'decoder.int8.onnx'; p = (Join-Path $Model 'decoder.int8.onnx') },
    @{ n = 'joiner.int8.onnx';  p = (Join-Path $Model 'joiner.int8.onnx') },
    @{ n = 'LICENSES.txt';      p = $lic }
)

$sha = [Security.Cryptography.SHA256]::Create()
$names = @(); $sizes = @(); $packed = @(); $hashes = @()
foreach ($it in $items) {
    $raw = [IO.File]::ReadAllBytes($it.p)
    $gz = Join-Path $Out ($it.n + '.gz')
    $fs = [IO.File]::Create($gz)
    $z = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
    $z.Write($raw, 0, $raw.Length)
    $z.Dispose(); $fs.Dispose()
    $h = -join ($sha.ComputeHash($raw) | ForEach-Object { $_.ToString('X2') })
    $names += $it.n; $sizes += $raw.Length; $packed += (Get-Item $gz).Length; $hashes += $h
    '{0,-20} {1,10} -> {2,10}  {3}' -f $it.n, $raw.Length, (Get-Item $gz).Length, $h
}
Remove-Item $lic
''
'Files   = { ' + (($names | ForEach-Object { '"' + $_ + '"' }) -join ', ') + ' };'
'Sizes   = { ' + ($sizes -join ', ') + ' };'
'GzSizes = { ' + ($packed -join ', ') + ' };'
'Sha256  ='
$hashes | ForEach-Object { '    "' + $_ + '",' }
'total download: {0} bytes ({1:N1} MB)' -f (($packed | Measure-Object -Sum).Sum), ((($packed | Measure-Object -Sum).Sum) / 1MB)
