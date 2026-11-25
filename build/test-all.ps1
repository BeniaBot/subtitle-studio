# test-all.ps1 - every test package, each with the check count it must reach.
# "Green with fewer checks is not green" (CLAUDE.md): a package that passes with
# fewer checks than expected, times out, or crashes counts as a failure.
# A stuck package is usually a .NET crash dialog - hang-report.ps1 prints it.
# ASCII only.  Usage: test-all.ps1 [-Only name,name] [-Timeout 900] [-AllowSkip]
# -AllowSkip (the clean test machine): a package that SAYS it skipped N checks ("SKIP ... (N ...)" - data that
# exists only on Binyamin's computer, like the focus-video shots) is green if passed + N reaches the count.
# A name like buttons@en runs test-buttons.ps1 -Lang en.
param([string]$Only = '', [int]$Timeout = 900, [switch]$AllowSkip)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
# Under a user with a Hebrew name, %TEMP% can be an 8.3 path (C:\Users\0AC8~1\...) that Windows PowerShell 5.1 cannot
# resolve: on the clean test machine (18.11) every package crashed in its cleanup ("An object at the specified path
# C:\Users\0AC8~1 does not exist") after its checks had passed. The same folder by its long name - the packages inherit it.
if ($env:TEMP -match '~') {
    $longTemp = Join-Path $env:LOCALAPPDATA 'Temp'
    if (Test-Path -LiteralPath $longTemp) { $env:TEMP = $longTemp; $env:TMP = $longTemp }
}
# The packages load dist\Subtext.exe from its bytes - it has no location then, so its engine (the ffmpeg at its end) is
# found through SUBSTUDIO_EXE; and on a machine where Subtext never ran nothing is unpacked yet: do it once here, the
# way the app does on its first run (the clean test machine, 18.11: every media package failed without it).
$env:SUBSTUDIO_EXE = Join-Path (Split-Path $here -Parent) 'dist\Subtext.exe'
try {
    $env:SUBSTUDIO_TEST = '1'
    $asm0 = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($env:SUBSTUDIO_EXE))
    $rtT = $asm0.GetType('SubtitleStudio.Runtime'); $bf = [Reflection.BindingFlags]'NonPublic,Public,Static'
    [void]$rtT.GetMethod('Prepare', $bf).Invoke($null, @())
    # and FIRST on PATH: a package that does not call Prepare finds ffmpeg through PATH (Ff.Locate) - on Binyamin's computer
    # that was a full 2025 build in Program Files, not the engine users get; on the clean machine there was none at all
    $ownFf = [string]$rtT.GetProperty('FfmpegPath', $bf).GetValue($null, $null)
    if ($ownFf -and (Test-Path -LiteralPath $ownFf)) { $env:PATH = (Split-Path $ownFf -Parent) + ';' + $env:PATH }
    else { Write-Host 'the engine was not unpacked - the packages may find another ffmpeg on PATH' }
} catch { Write-Host ('the engine was not unpacked: ' + $_.Exception.Message) }
$expect = [ordered]@{
    'logic' = 224; 'autotime' = 36; 'scenecuts' = 47; 'list' = 12; 'roundtrip' = 15
    'update-notes' = 31; 'ui' = 65; 'layout' = 125; 'project' = 115; 'screens' = 45
    'fuzz' = 11; 'long' = 15; 'stt' = 146; 'errors' = 41; 'buttons' = 14
    'source' = 9; 'audio' = 27; 'qa' = 106; 'spell' = 75; 'release' = 67; 'settings' = 19; 'subs' = 68; 'cut' = 57; 'import' = 34; 'assistant' = 104; 'chatflow' = 52; 'local' = 166
    # name@en = the same package with -Lang en: the whole layout is mirrored there
    'buttons@en' = 14; 'layout@en' = 125; 'screens@en' = 45
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
        # UTF-8 out: under a system code page without Hebrew (an English Windows) every Hebrew check name came back as "?"
        $a = '-NoProfile -ExecutionPolicy Bypass -Command "[Console]::OutputEncoding = [Text.Encoding]::UTF8; & ''' + $f + "'" + $(if ($l) { ' -Lang ' + $l } else { '' }) + '"'
        # stdin from an empty file: otherwise the package (and an ffmpeg without -nostdin in it) inherits the
        # job's own stdin - the pipe PowerShell talks to the job over - and ffmpeg sat reading it (8.10)
        [IO.File]::WriteAllText($log + '.in', '')
        $p = Start-Process powershell -ArgumentList $a -RedirectStandardInput ($log + '.in') -RedirectStandardOutput $log -RedirectStandardError ($log + '.err') -NoNewWindow -PassThru
        $p.WaitForExit()
        Get-CimInstance Win32_Process -Filter ("Name='ffmpeg.exe' AND ParentProcessId=" + $p.Id) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $o = ''
        foreach ($x in $log, ($log + '.err'), ($log + '.in')) {
            try { $fs = New-Object IO.FileStream($x, 'Open', 'Read', 'ReadWrite'); $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $o += $sr.ReadToEnd(); $sr.Dispose() } catch { }
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
    $skipped = 0
    if ($AllowSkip) { foreach ($sm in [regex]::Matches($out, '(?m)^\s*SKIP\b.*?\((\d+)\b')) { $skipped += [int]$sm.Groups[1].Value } }
    $ok = ($state -eq 'done') -and ($f -eq 0) -and ($p -ge 0) -and ($want -lt 0 -or $p + $skipped -ge $want)
    $why = ''
    if ($state -ne 'done') { $why = 'timed out' }
    elseif ($p -lt 0) { $why = 'no summary line (crashed?)' }
    elseif ($f -gt 0) { $why = "$f failed" }
    elseif ($want -ge 0 -and $p -lt $want) { $why = "only $p of $want checks" }
    if (-not $ok) {
        $bad++
        $fails = @($out -split "`n" | Where-Object { $_ -match '^\s*FAIL' } | Select-Object -First 6)
        try { [IO.File]::WriteAllText((Join-Path $env:TEMP ('ss-testall-fail-' + ($n -replace '[^\w@-]', '_') + '.txt')), [string]$out, (New-Object Text.UTF8Encoding $true)) } catch { }
        $why += $(if ($fails.Count) { "`n      " + (($fails | ForEach-Object { $_.Trim() }) -join "`n      ") } else { '' })
    }
    if ($p -gt 0) { $total += $p }
    $secs = [int]($sw.Elapsed.TotalSeconds - $t0)
    $line = ('{0,-13} {1,5} / {2,-5} {3,5}s  {4}' -f $n, $p, $want, $secs, $(if ($ok) { 'ok' + $(if ($skipped -gt 0 -and $p -lt $want) { " ($skipped skipped - said so)" } else { '' }) } else { 'FAIL  ' + $why }))
    Write-Host $line
}
Write-Host ''
Write-Host ('{0} packages, {1} checks, {2} failing, {3} min' -f $names.Count, $total, $bad, [int]$sw.Elapsed.TotalMinutes)
if ($bad -gt 0) { exit 1 }
