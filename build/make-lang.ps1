# make-lang.ps1 - turn build\lang\en.tsv into src\LangEn.cs
# ASCII only on purpose (no BOM trap). Run after editing the table.
param([switch]$Quiet, [string]$Batch = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tsv  = Join-Path $root 'build\lang\en.tsv'
$out  = Join-Path $root 'src\LangEn.cs'

# the TSV carries \n \t \\ as two characters; the runtime value is the real one
function Dec([string]$s)
{
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $s.Length; $i++)
    {
        if ($s[$i] -eq '\' -and $i + 1 -lt $s.Length)
        {
            $i++
            switch ($s[$i])
            {
                'n'     { [void]$sb.Append("`n") }
                't'     { [void]$sb.Append("`t") }
                '\'     { [void]$sb.Append('\') }
                default { [void]$sb.Append('\'); [void]$sb.Append($s[$i]) }
            }
            continue
        }
        [void]$sb.Append($s[$i])
    }
    return $sb.ToString()
}

# a C# string literal for a value that may hold quotes, backslashes or newlines
function Lit([string]$s)
{
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($c in $s.ToCharArray())
    {
        switch ($c)
        {
            '"'      { [void]$sb.Append('\"') }
            '\'      { [void]$sb.Append('\\') }
            "`n"     { [void]$sb.Append('\n') }
            "`r"     { [void]$sb.Append('\r') }
            "`t"     { [void]$sb.Append('\t') }
            default
            {
                # keep Hebrew and punctuation as real characters: the compiler
                # reads the file as UTF-8 (/codepage:65001), and \u escapes
                # would make the generated file unreadable
                if ([int]$c -lt 32) { [void]$sb.Append('\u' + ([int]$c).ToString('x4')) }
                else { [void]$sb.Append($c) }
            }
        }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

if (-not (Test-Path $tsv)) { throw ('missing ' + $tsv) }

# -Batch merges a small he<TAB>en file into the table, in place. Translating
# 1,585 strings happens in slices, and rewriting the whole table each time
# would be both slow and a good way to lose work.
if ($Batch -ne '')
{
    if (-not (Test-Path $Batch)) { throw ('missing ' + $Batch) }
    $add = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($line in [IO.File]::ReadAllLines($Batch, [Text.Encoding]::UTF8))
    {
        if ($line.Trim().Length -eq 0 -or $line.StartsWith('#')) { continue }
        $t = $line -split "`t", 2
        if ($t.Length -eq 2 -and $t[1].Trim().Length -gt 0) { $add[$t[0]] = $t[1].Trim() }
    }
    $hit = 0; $miss = New-Object System.Collections.ArrayList
    $lines = [IO.File]::ReadAllLines($tsv, [Text.Encoding]::UTF8)
    $seenKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    for ($i = 0; $i -lt $lines.Length; $i++)
    {
        $l = $lines[$i]
        if ($l.Trim().Length -eq 0 -or $l.StartsWith('#')) { continue }
        $t = $l -split "`t", 2
        if ($t.Length -lt 1) { continue }
        [void]$seenKeys.Add($t[0])
        if ($add.ContainsKey($t[0])) { $lines[$i] = $t[0] + "`t" + $add[$t[0]]; $hit++ }
    }
    foreach ($k in $add.Keys) { if (-not $seenKeys.Contains($k)) { [void]$miss.Add($k) } }
    [IO.File]::WriteAllText($tsv, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $true))
    if (-not $Quiet)
    {
        Write-Host ''
        Write-Host ('  merged     : ' + $hit)
        if ($miss.Count -gt 0)
        {
            Write-Host ('  NOT IN TABLE (' + $miss.Count + ') - check the Hebrew is byte for byte:')
            foreach ($m in $miss) { Write-Host ('    ' + $m) }
        }
    }
}

$pairs = New-Object System.Collections.ArrayList
$seen  = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$dupes = 0
$blank = 0
$lineNo = 0
foreach ($line in [IO.File]::ReadAllLines($tsv, [Text.Encoding]::UTF8))
{
    $lineNo++
    if ($lineNo -eq 1 -and $line -like 'he*') { continue }   # header
    if ($line.Trim().Length -eq 0 -or $line.StartsWith('#')) { continue }
    $t = $line -split "`t", 2
    if ($t.Length -lt 2) { $blank++; continue }
    $he = Dec $t[0]
    $en = Dec $t[1]
    if ($en.Trim().Length -eq 0) { $blank++; continue }
    if (-not $seen.Add($he)) { $dupes++; continue }
    [void]$pairs.Add(@($he, $en))
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('using System.Collections.Generic;')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('namespace SubtitleStudio')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    /// <summary>GENERATED FROM build\lang\en.tsv BY build\make-lang.ps1.')
[void]$sb.AppendLine('    /// Do not edit by hand: edit the table and run the script.</summary>')
[void]$sb.AppendLine('    internal static class LangEn')
[void]$sb.AppendLine('    {')

$chunk = 300
$parts = [Math]::Max(1, [Math]::Ceiling($pairs.Count / [double]$chunk))
for ($p = 0; $p -lt $parts; $p++)
{
    [void]$sb.AppendLine('        private static readonly string[] P' + $p + ' = new string[] {')
    $from = $p * $chunk
    $to   = [Math]::Min($pairs.Count, $from + $chunk) - 1
    for ($k = $from; $k -le $to; $k++)
    {
        [void]$sb.AppendLine('            ' + (Lit $pairs[$k][0]) + ', ' + (Lit $pairs[$k][1]) + ',')
    }
    [void]$sb.AppendLine('        };')
}

[void]$sb.AppendLine('')
[void]$sb.AppendLine('        public static void Fill(Dictionary<string, string> d)')
[void]$sb.AppendLine('        {')
for ($p = 0; $p -lt $parts; $p++) { [void]$sb.AppendLine('            Add(d, P' + $p + ');') }
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('        private static void Add(Dictionary<string, string> d, string[] a)')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            for (int i = 0; i + 1 < a.Length; i += 2) d[a[i]] = a[i + 1];')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')

[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object Text.UTF8Encoding $false))

if (-not $Quiet)
{
    Write-Host ''
    Write-Host ('  translated : ' + $pairs.Count)
    if ($blank -gt 0) { Write-Host ('  empty      : ' + $blank) }
    if ($dupes -gt 0) { Write-Host ('  duplicate  : ' + $dupes + '  (first one wins)') }
    Write-Host ('  -> ' + $out)
    Write-Host ''
}
