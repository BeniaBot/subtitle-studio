# lang-fill.ps1 - translate build\lang\en.tsv in slices, by line number.
# Retyping a Hebrew key byte for byte is how a translation gets lost (one
# different geresh and Lang.T never finds it), so the slices are keyed by the
# line number in the table instead:
#   lang-fill.ps1 -Export -Count 150            the next 150 empty rows, with their file
#   lang-fill.ps1 -Import slice.txt             lines of  <lineNo><TAB><english>
# -Import checks every {n} against the Hebrew, refuses the whole slice on any
# mismatch, then regenerates src\LangEn.cs through make-lang.ps1.
# The line numbers hold only while the table is not rebuilt (-Skeleton).
# ASCII only.
param([switch]$Export, [int]$Count = 150, [int]$From = 0, [string]$Import = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tsv = Join-Path $root 'build\lang\en.tsv'
$lines = [IO.File]::ReadAllLines($tsv, [Text.Encoding]::UTF8)

function Holes([string]$s) {
    $m = [regex]::Matches($s, '\{(\d+)[^{}]*\}')
    return (($m | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique) -join ',')
}

if ($Export) {
    $sb = New-Object Text.StringBuilder
    $file = ''; $n = 0
    for ($i = 0; $i -lt $lines.Length -and $n -lt $Count; $i++) {
        $l = $lines[$i]
        if ($l.StartsWith('# ---- ')) { $file = $l; continue }
        if ($i + 1 -lt $From -or $l.StartsWith('#') -or $l.Trim().Length -eq 0 -or $i -eq 0) { continue }
        $p = $l -split "`t", 2
        if ($p.Length -eq 2 -and $p[1].Trim().Length -gt 0) { continue }
        if ($file -ne '') { [void]$sb.AppendLine($file); $file = '' }
        [void]$sb.AppendLine(($i + 1).ToString() + "`t" + $p[0])
        $n++
    }
    if ($Out -ne '') { [IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object Text.UTF8Encoding $true)); Write-Host "$n rows -> $Out" }
    else { Write-Host $sb.ToString() }
    exit 0
}

if ($Import -ne '') {
    $set = @{}; $bad = @()
    foreach ($l in [IO.File]::ReadAllLines($Import, [Text.Encoding]::UTF8)) {
        if ($l.Trim().Length -eq 0 -or $l.StartsWith('#')) { continue }
        # <lineNo><TAB>english  or  <lineNo> | english  (the first separator only:
        # an English file filter keeps its own "|")
        # Spaces at the ends are meaningful (" - mono" is glued to a value), so
        # nothing is trimmed: a value with a space at either end is written in
        # double quotes, and the quotes are taken off.
        $m = [regex]::Match($l, '^\s*(\d+)\s*(?:\t|\|) ?(.*?)\r?$')
        if (-not $m.Success) { $bad += "not <lineNo> | <english>: $l"; continue }
        $no = [int]$m.Groups[1].Value; $en = $m.Groups[2].Value
        if ($en.Length -ge 2 -and $en.StartsWith('"') -and $en.EndsWith('"')) { $en = $en.Substring(1, $en.Length - 2) }
        if ($en.Trim().Length -eq 0) { $bad += "line ${no}: empty English"; continue }
        if ($no -lt 2 -or $no -gt $lines.Length) { $bad += "no such line $no"; continue }
        $row = $lines[$no - 1]
        if ($row.StartsWith('#') -or $row.Trim().Length -eq 0) { $bad += "line $no is not a string row"; continue }
        $he = ($row -split "`t", 2)[0]
        if ((Holes $he) -ne (Holes $en)) { $bad += ('line {0}: placeholders differ  he[{1}] en[{2}]  {3}' -f $no, (Holes $he), (Holes $en), $en) }
        if (($he -match '^\s') -ne ($en -match '^\s') -or ($he -match '\s$') -ne ($en -match '\s$')) { $bad += ('line {0}: a space at the start or end differs from the Hebrew - quote it: "{1}"' -f $no, $en) }
        foreach ($esc in '\r\n', '\n') { if (([regex]::Matches($he, [regex]::Escape($esc))).Count -ne ([regex]::Matches($en, [regex]::Escape($esc))).Count) { $bad += ('line {0}: line breaks ({1}) differ from the Hebrew' -f $no, $esc); break } }
        $set[$no] = $en
    }
    if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "  !! $_" }; Write-Host 'nothing written'; exit 1 }
    foreach ($k in $set.Keys) { $he = ($lines[$k - 1] -split "`t", 2)[0]; $lines[$k - 1] = $he + "`t" + $set[$k] }
    [IO.File]::WriteAllText($tsv, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $true))
    Write-Host ('  filled ' + $set.Count + ' rows')
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-lang.ps1')
    exit 0
}
Write-Host 'use -Export or -Import'
