# test-all.ps1 - every test package, each with the check count it must reach.
# "Green with fewer checks is not green" (CLAUDE.md): a package that passes with
# fewer checks than expected, times out, or crashes counts as a failure.
# A stuck package is usually a .NET crash dialog - hang-report.ps1 prints it.
# ASCII only.  Usage: test-all.ps1 [-Only name,name] [-Timeout 900]
# A name like buttons@en runs test-buttons.ps1 -Lang en.
param([string]$Only = '', [int]$Timeout = 900)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$expect = [ordered]@{
    'logic' = 192; 'autotime' = 21; 'scenecuts' = 47; 'list' = 12; 'roundtrip' = 15
    'update-notes' = 30; 'ui' = 45; 'layout' = 95; 'project' = 105; 'screens' = 32
    'fuzz' = 11; 'long' = 15; 'stt' = 74; 'errors' = 36; 'buttons' = 10
    'source' = 7; 'qa' = 60; 'spell' = 59; 'release' = 42; 'settings' = 9; 'subs' = 21
    # name@en = the same package with -Lang en: the whole layout is mirrored there
    'buttons@en' = 10; 'layout@en' = 95; 'screens@en' = 32
}
$names = @($expect.Keys)
if ($Only -ne '') { $names = @($Only -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) }

$rows = @(); $total = 0; $bad = 0
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($n in $names) {
    $parts = $n -split '@', 2
    $file = Join-Path $here ('test-' + $parts[0] + '.ps1')
    $lang = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    $t0 = $sw.Elapsed.TotalSeconds
    $job = Start-Job -ArgumentList $file, $lang { param($f, $l)
        if ($l) { powershell -NoProfile -ExecutionPolicy Bypass -File $f -Lang $l 2>&1 | Out-String }
        else { powershell -NoProfile -ExecutionPolicy Bypass -File $f 2>&1 | Out-String } }
    $out = ''; $state = 'done'
    if (Wait-Job $job -Timeout $Timeout) { $out = Receive-Job $job } else {
        $state = 'TIMEOUT'
        Stop-Job $job
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'hang-report.ps1') -Kill | Out-Host
    }
    Remove-Job $job -Force
    $m = [regex]::Matches($out, '(\d+) passed, (\d+) failed')
    $p = -1; $f = -1
    if ($m.Count -gt 0) { $last = $m[$m.Count - 1]; $p = [int]$last.Groups[1].Value; $f = [int]$last.Groups[2].Value }
    $want = if ($expect.Contains($n)) { $expect[$n] } else { -1 }
    $ok = ($state -eq 'done') -and ($f -eq 0) -and ($p -ge 0) -and ($want -lt 0 -or $p -ge $want)
    $why = ''
    if ($state -ne 'done') { $why = 'timed out' }
    elseif ($p -lt 0) { $why = 'no summary line (crashed?)' }
    elseif ($f -gt 0) { $why = "$f failed" }
    elseif ($want -ge 0 -and $p -lt $want) { $why = "only $p of $want checks" }
    if (-not $ok) {
        $bad++
        $fails = @($out -split "`n" | Where-Object { $_ -match '^\s*FAIL' } | Select-Object -First 6)
        $why += $(if ($fails.Count) { "`n      " + (($fails | ForEach-Object { $_.Trim() }) -join "`n      ") } else { '' })
    }
    if ($p -gt 0) { $total += $p }
    $secs = [int]($sw.Elapsed.TotalSeconds - $t0)
    $line = ('{0,-13} {1,5} / {2,-5} {3,5}s  {4}' -f $n, $p, $want, $secs, $(if ($ok) { 'ok' } else { 'FAIL  ' + $why }))
    Write-Host $line
}
Write-Host ''
Write-Host ('{0} packages, {1} checks, {2} failing, {3} min' -f $names.Count, $total, $bad, [int]$sw.Elapsed.TotalMinutes)
if ($bad -gt 0) { exit 1 }
