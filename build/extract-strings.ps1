# extract-strings.ps1 - scan src\*.cs and list every Hebrew string literal.
# ASCII only on purpose: no BOM trap, and the Hebrew range is written as \u escapes.
# Output: build\strings.tsv  (file, line, kind, context, text)
param([switch]$Quiet, [switch]$Skeleton)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root 'src'
$out  = Join-Path $root 'build\strings.tsv'

$heb = [regex]'[֐-׿]'

# Walk the file as a tiny C# lexer: we must know whether a quote opens a string
# or sits inside a comment, and whether the string is verbatim (@"..").
function Scan([string]$text)
{
    $res = New-Object System.Collections.ArrayList
    $i = 0; $line = 1; $n = $text.Length
    while ($i -lt $n)
    {
        $c = $text[$i]
        if ($c -eq "`n") { $line++; $i++; continue }

        # line comment
        if ($c -eq '/' -and $i + 1 -lt $n -and $text[$i+1] -eq '/')
        {
            while ($i -lt $n -and $text[$i] -ne "`n") { $i++ }
            continue
        }
        # block comment
        if ($c -eq '/' -and $i + 1 -lt $n -and $text[$i+1] -eq '*')
        {
            $i += 2
            while ($i + 1 -lt $n -and -not ($text[$i] -eq '*' -and $text[$i+1] -eq '/'))
            {
                if ($text[$i] -eq "`n") { $line++ }
                $i++
            }
            $i += 2
            continue
        }
        # char literal
        if ($c -eq "'")
        {
            $i++
            while ($i -lt $n -and $text[$i] -ne "'")
            {
                if ($text[$i] -eq '\') { $i++ }
                $i++
            }
            $i++
            continue
        }
        # verbatim string
        if ($c -eq '@' -and $i + 1 -lt $n -and $text[$i+1] -eq '"')
        {
            $start = $i; $startLine = $line
            $i += 2
            $sb = New-Object System.Text.StringBuilder
            while ($i -lt $n)
            {
                if ($text[$i] -eq '"')
                {
                    if ($i + 1 -lt $n -and $text[$i+1] -eq '"') { [void]$sb.Append('"'); $i += 2; continue }
                    break
                }
                if ($text[$i] -eq "`n") { $line++ }
                [void]$sb.Append($text[$i]); $i++
            }
            $i++
            [void]$res.Add(@{ Line = $startLine; Text = $sb.ToString(); Start = $start; Verbatim = $true })
            continue
        }
        # regular string
        if ($c -eq '"')
        {
            $start = $i; $startLine = $line
            $i++
            $sb = New-Object System.Text.StringBuilder
            while ($i -lt $n -and $text[$i] -ne '"')
            {
                if ($text[$i] -eq '\')
                {
                    # decode to the RUNTIME value: that is what Lang.T() will be handed
                    $i++
                    if ($i -ge $n) { break }
                    $e = $text[$i]; $i++
                    switch ($e)
                    {
                        'n'  { [void]$sb.Append("`n") }
                        't'  { [void]$sb.Append("`t") }
                        'r'  { [void]$sb.Append("`r") }
                        '0'  { [void]$sb.Append([char]0) }
                        '"'  { [void]$sb.Append('"') }
                        "'"  { [void]$sb.Append("'") }
                        '\'  { [void]$sb.Append('\') }
                        'u'  { if ($i + 3 -lt $n) { [void]$sb.Append([char][Convert]::ToInt32($text.Substring($i,4),16)); $i += 4 } }
                        default { [void]$sb.Append($e) }
                    }
                    continue
                }
                if ($text[$i] -eq "`n") { $line++ }
                [void]$sb.Append($text[$i]); $i++
            }
            $i++
            [void]$res.Add(@{ Line = $startLine; Text = $sb.ToString(); Start = $start; Verbatim = $false })
            continue
        }
        $i++
    }
    return ,$res
}

# What is this string for? Only "ui" needs translating.
function Classify([string]$ctx, [string]$file)
{
    if ($ctx -match 'Ai\.Log\s*\(|\bDbg\s*\(|Log\s*\(\s*$')     { return 'log' }
    if ($ctx -match 'const\s+string')                            { return 'const' }
    if ($ctx -match '^\s*(case|default)\b')                      { return 'switch' }
    if ($file -match 'AssemblyInfo')                             { return 'meta' }
    return 'ui'
}

$rows = New-Object System.Collections.ArrayList
$files = @(Get-ChildItem -Path $src -Filter *.cs -Recurse | Sort-Object FullName)
foreach ($f in $files)
{
    $text = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
    $lines = $text -split "`n"
    foreach ($s in (Scan $text))
    {
        if (-not $heb.IsMatch($s.Text)) { continue }
        $li  = [int]$s.Line - 1
        $ctx = ''
        if ($li -ge 0 -and $li -lt $lines.Length) { $ctx = ($lines[$li] -replace "`r", '').Trim() }
        $rel = $f.FullName.Substring($src.Length + 1)
        [void]$rows.Add([PSCustomObject]@{
            File = $rel
            Line = $s.Line
            Kind = (Classify $ctx $rel)
            Text = $s.Text
            Ctx  = $ctx
        })
    }
}

# The TSV cannot hold a real tab or newline, so the text column is encoded.
# build\make-lang.ps1 decodes it back before writing the C# literal.
function Enc([string]$s)
{
    return $s.Replace('\', '\\').Replace("`r", '').Replace("`n", '\n').Replace("`t", '\t')
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("File`tLine`tKind`tText`tContext")
foreach ($r in $rows)
{
    $c = $r.Ctx -replace "`t", ' '
    [void]$sb.AppendLine(($r.File + "`t" + $r.Line + "`t" + $r.Kind + "`t" + (Enc $r.Text) + "`t" + $c))
}
[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object Text.UTF8Encoding $true))

# -Skeleton: rebuild build\lang\en.tsv in source order, keeping what is
# already translated. Strings that left the code move to a graveyard at the
# end - a rename should not throw away the English.
if ($Skeleton)
{
    $lang = Join-Path $root 'build\lang\en.tsv'
    $old  = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    if (Test-Path $lang)
    {
        foreach ($line in [IO.File]::ReadAllLines($lang, [Text.Encoding]::UTF8))
        {
            if ($line.Trim().Length -eq 0) { continue }
            $bare = $line
            if ($bare.StartsWith('#~ ')) { $bare = $bare.Substring(3) }   # graveyard row
            elseif ($bare.StartsWith('#')) { continue }
            $t = $bare -split "`t", 2
            if ($t.Length -eq 2 -and $t[1].Trim().Length -gt 0) { $old[$t[0]] = $t[1] }
        }
    }

    $sb2 = New-Object System.Text.StringBuilder
    [void]$sb2.AppendLine("he`ten")
    [void]$sb2.AppendLine('# Hebrew is the key. An empty English column means: still shows Hebrew.')
    [void]$sb2.AppendLine('# Source order, so the strings of one screen sit together.')
    $used = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $done = 0; $todo = 0
    $lastFile = ''
    foreach ($r in $rows)
    {
        if ($r.Kind -ne 'ui') { continue }
        $k = Enc $r.Text
        if (-not $used.Add($k)) { continue }
        if ($r.File -ne $lastFile)
        {
            $lastFile = $r.File
            [void]$sb2.AppendLine('')
            [void]$sb2.AppendLine('# ---- ' + $lastFile + ' ----')
        }
        $v = ''
        if ($old.ContainsKey($k)) { $v = $old[$k] }
        if ($v.Trim().Length -gt 0) { $done++ } else { $todo++ }
        [void]$sb2.AppendLine($k + "`t" + $v)
    }

    $orphans = @($old.Keys | Where-Object { -not $used.Contains($_) } | Sort-Object)
    if ($orphans.Count -gt 0)
    {
        [void]$sb2.AppendLine('')
        [void]$sb2.AppendLine('# ---- no longer in the source (kept in case it comes back) ----')
        foreach ($k in $orphans) { [void]$sb2.AppendLine('#~ ' + $k + "`t" + $old[$k]) }
    }

    $dir = Split-Path -Parent $lang
    if (-not (Test-Path $dir)) { [void](New-Item -ItemType Directory -Path $dir) }
    [IO.File]::WriteAllText($lang, $sb2.ToString(), (New-Object Text.UTF8Encoding $true))
    if (-not $Quiet)
    {
        Write-Host ''
        Write-Host ('  translated : ' + $done)
        Write-Host ('  to do      : ' + $todo)
        if ($orphans.Count -gt 0) { Write-Host ('  retired    : ' + $orphans.Count) }
        Write-Host ('  -> ' + $lang)
    }
}

if (-not $Quiet)
{
    $ui = @($rows | Where-Object { $_.Kind -eq 'ui' })
    Write-Host ''
    Write-Host ('  total literals with Hebrew : ' + $rows.Count)
    Write-Host ('  user-facing (ui)           : ' + $ui.Count)
    Write-Host ('  unique ui strings          : ' + (@($ui | Select-Object -ExpandProperty Text -Unique)).Count)
    Write-Host ''
    foreach ($g in ($rows | Group-Object Kind | Sort-Object Count -Descending))
    {
        Write-Host ('    ' + $g.Name.PadRight(8) + ' ' + $g.Count)
    }
    Write-Host ''
    Write-Host '  top files (ui only):'
    foreach ($g in ($ui | Group-Object File | Sort-Object Count -Descending | Select-Object -First 12))
    {
        Write-Host ('    ' + $g.Name.PadRight(22) + ' ' + $g.Count)
    }
    Write-Host ''
    Write-Host ('  -> ' + $out)
}
