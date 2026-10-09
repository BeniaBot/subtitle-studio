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
    'logic' = 207; 'autotime' = 31; 'scenecuts' = 47; 'list' = 12; 'roundtrip' = 15
    'update-notes' = 30; 'ui' = 46; 'layout' = 95; 'project' = 106; 'screens' = 35
    'fuzz' = 11; 'long' = 15; 'stt' = 125; 'errors' = 38; 'buttons' = 12
    'source' = 7; 'qa' = 100; 'spell' = 59; 'release' = 67; 'settings' = 19; 'subs' = 26; 'cut' = 43; 'import' = 33
    # name@en = the same package with -Lang en: the whole layout is mirrored there
    'buttons@en' = 12; 'layout@en' = 95; 'screens@en' = 35
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
    # Output goes to a FILE, and we wait for the PROCESS - not for a pipe to reach EOF. An ffmpeg whose parent
    # died while creating it stays frozen forever (one thread, no CPU) and inherits every inheritable handle,
    # the package's stdout pipe too: twice on 8.10 the run waited ten minutes on such a pipe. Leftover
    # ffmpeg children of the package (and only those - never the user's own Subtext) are killed afterwards.
    $log = Join-Path $env:TEMP ('ss-testall-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
    $job = Start-Job -ArgumentList $file, $lang, $log { param($f, $l, $log)
        $a = '-NoProfile -ExecutionPolicy Bypass -File "' + $f + '"' + $(if ($l) { ' -Lang ' + $l } else { '' })
        # stdin from an empty file: otherwise the package (and an ffmpeg without -nostdin in it) inherits the
        # job's own stdin - the pipe PowerShell talks to the job over - and ffmpeg sat reading it (8.10)
        [IO.File]::WriteAllText($log + '.in', '')
        $p = Start-Process powershell -ArgumentList $a -RedirectStandardInput ($log + '.in') -RedirectStandardOutput $log -RedirectStandardError ($log + '.err') -NoNewWindow -PassThru
        $p.WaitForExit()
        Get-CimInstance Win32_Process -Filter ("Name='ffmpeg.exe' AND ParentProcessId=" + $p.Id) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $o = ''
        foreach ($x in $log, ($log + '.err'), ($log + '.in')) {
            try { $fs = New-Object IO.FileStream($x, 'Open', 'Read', 'ReadWrite'); $sr = New-Object IO.StreamReader($fs); $o += $sr.ReadToEnd(); $sr.Dispose() } catch { }
            try { [IO.File]::Delete($x) } catch { }
        }
        $o }
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
