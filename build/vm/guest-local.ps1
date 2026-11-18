# Runs INSIDE the clean test machine (build\test-clean-vm.ps1 copies it there): the local models for real - downloads from
# GitHub into the user's real %LOCALAPPDATA%, transcription of a Hebrew clip, translation of English cues - with the app's
# own code (the EXE loaded as an assembly), and what the processes loaded. ASCII only (PowerShell 5.1, no BOM).
#   guest-local.ps1 -Exe <Subtext.exe> -Wav <hebrew clip> -Cues <cues_en.json> -Out <report.txt>
param([string]$Exe, [string]$Wav, [string]$Cues, [string]$Out)
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$log = New-Object Collections.Generic.List[string]
function Say($s) { $log.Add([string]$s); [IO.File]::WriteAllLines($Out, $log, (New-Object Text.UTF8Encoding($true))) }
try {
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Exe))
    $SF = [Reflection.BindingFlags]'NonPublic,Public,Static'; $IF = [Reflection.BindingFlags]'NonPublic,Public,Instance'
    function T($n) { $asm.GetType("SubtitleStudio.$n") }
    [void](T 'Runtime').GetMethod('Prepare', $SF).Invoke($null, @())
    $lm = T 'LocalModel'; $cat = T 'ModelCatalog'
    # the REAL models folder of this user (SUBSTUDIO_TEST alone puts it under TEMP - an 8.3 path for a Hebrew user, which
    # would hide the very thing tested here: engines running from a folder with Hebrew letters in its path)
    $lm.GetField('FolderOverride', $SF).SetValue($null, [string](Join-Path $env:LOCALAPPDATA 'SubtitleStudio\models\stt-he'))
    Say ("models folder: " + $cat.GetProperty('Root', $SF).GetValue($null, $null))
    Say ("system msvcp140.dll: " + (Test-Path "$env:WINDIR\System32\msvcp140.dll") + ", vcruntime140.dll: " + (Test-Path "$env:WINDIR\System32\vcruntime140.dll"))

    # ---- transcription pack (0.8.45) ----
    $prog = [Action[long, long]]{ param($d, $t) }
    $argv = New-Object object[] 3; $argv[0] = $prog; $argv[1] = $null; $argv[2] = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $ok = $lm.GetMethod('Download', $SF).Invoke($null, $argv)
    Say ("stt download: ok=$ok err=[" + $argv[2] + "] {0:N1}s, installed=" -f $sw.Elapsed.TotalSeconds)
    Say ("stt installed: " + $lm.GetProperty('Installed', $SF).GetValue($null, $null))
    $mi = (T 'Ff').GetMethod('ProbeFile', $SF).Invoke($null, @([string]$Wav))
    $loc = (T 'Stt').GetProperty('Local', $SF).GetValue($null, $null)
    $m = $loc.GetType().GetMethods($IF) | Where-Object { $_.Name -eq 'RunFile' -and $_.GetParameters().Count -eq 7 }
    $a7 = New-Object object[] 7; $a7[0] = [string]$Wav; $a7[1] = [long]$mi.DurationMs; $a7[2] = $null; $a7[3] = $null; $a7[4] = $null; $a7[5] = [long]0; $a7[6] = [long]0
    $sw.Restart()
    $res = $m.Invoke($loc, $a7)
    $rt = $res.GetType(); $cl = $rt.GetField('Cues').GetValue($res)
    Say ("stt run: {0:N1}s for {1:N0}s of audio, cues {2}, error [{3}]" -f $sw.Elapsed.TotalSeconds, ($mi.DurationMs / 1000.0), $cl.Count, $rt.GetField('Error').GetValue($res))
    $i = 0; foreach ($c in $cl) { if ($i++ -ge 6) { break }; Say ("   " + $c.Text) }

    # ---- translation packs (0.8.46) ----
    $pk = $cat.GetMethod('ById', $SF).Invoke($null, @('mt-en-he-quick'))
    $argv = New-Object object[] 4; $argv[0] = $pk; $argv[1] = $prog; $argv[2] = $null; $argv[3] = $null
    $sw.Restart()
    $ok = $cat.GetMethod('Download', $SF).Invoke($null, $argv)
    Say ("mt download: ok=$ok err=[" + $argv[3] + "] {0:N1}s" -f $sw.Elapsed.TotalSeconds)
    $cueT = T 'Cue'
    $list = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($cueT))
    foreach ($c in (Get-Content $Cues -Raw -Encoding UTF8 | ConvertFrom-Json)) { $x = [Activator]::CreateInstance($cueT); $x.Start = [long]([double]$c.start * 1000); $x.End = [long]([double]$c.end * 1000); $x.Text = [string]$c.en; [void]$list.Add($x) }
    $lt = T 'LocalTranslator'
    $a6 = New-Object object[] 6; $a6[0] = $list; $a6[1] = 'Vivobook, Asus'; $a6[2] = $null; $a6[3] = $null; $a6[4] = $null; $a6[5] = $null
    $sw.Restart()
    $tr = $lt.GetMethod('Run', $SF).Invoke($null, $a6)
    Say ("mt run: {0:N1}s for {1} cues, error [{2}], stopped {3}" -f $sw.Elapsed.TotalSeconds, @($tr).Count, $a6[4], $a6[5])
    for ($k = 0; $k -lt 6 -and $k -lt @($tr).Count; $k++) { Say ("   " + $list[$k].Text + "  =>  " + $tr[$k]) }
    $py = Get-Process python -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($py) {
        Say ("python: " + $py.Path + ", peak " + [math]::Round($py.PeakWorkingSet64 / 1MB) + " MB")
        Say ("runtime dlls: " + (($py.Modules | Where-Object { $_.ModuleName -match '^(msvcp140|vcruntime140|vcruntime140_1|onnxruntime)\.dll$' } | ForEach-Object { $_.FileName }) -join ' | '))
    }
    $lt.GetMethod('Stop', $SF).Invoke($null, @())

    # ---- the accurate model (TranslateGemma 4B, 2.4 GB in two parts) ----
    $hv = $cat.GetMethod('ById', $SF).Invoke($null, @('mt-translategemma-4b'))
    Say ("heavy fits this machine: " + $hv.Fits + " (" + $cat.GetMethod('FitText', $SF).Invoke($null, @($hv)) + ")")
    $argv = New-Object object[] 4; $argv[0] = $hv; $argv[1] = $prog; $argv[2] = $null; $argv[3] = $null
    $sw.Restart()
    $ok = $cat.GetMethod('Download', $SF).Invoke($null, $argv)
    Say ("heavy download: ok=$ok err=[" + $argv[3] + "] {0:N1}s" -f $sw.Elapsed.TotalSeconds)
    $few = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($cueT))
    for ($k = 0; $k -lt 12 -and $k -lt $list.Count; $k++) { [void]$few.Add($list[$k]) }
    $a9 = New-Object object[] 9; $a9[0] = 'mt-translategemma-4b'; $a9[1] = 'en'; $a9[2] = 'he'; $a9[3] = $few; $a9[4] = ''; $a9[5] = $null; $a9[6] = $null; $a9[7] = $null; $a9[8] = $null
    $peak = 0
    $sw.Restart()
    $job = [powershell]::Create().AddScript({ param($lt, $SF, $a9) $lt.GetMethod('RunWith', $SF).Invoke($null, $a9) }).AddArgument($lt).AddArgument($SF).AddArgument($a9)
    $h = $job.BeginInvoke()
    while (-not $h.IsCompleted) {
        Start-Sleep -Milliseconds 500
        $ls = Get-Process llama-server -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($ls -and $ls.WorkingSet64 -gt $peak) { $peak = $ls.WorkingSet64 }
    }
    # the List comes back unrolled into its strings
    $htr = @($job.EndInvoke($h))
    Say ("heavy run: {0:N1}s for {1} cues, error [{2}], llama-server peak {3} MB" -f $sw.Elapsed.TotalSeconds, $few.Count, $a9[7], [math]::Round($peak / 1MB))
    for ($k = 0; $k -lt 6 -and $k -lt @($htr).Count; $k++) { Say ("   " + $few[$k].Text + "  =>  " + $htr[$k]) }
    $ls = Get-Process llama-server -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($ls) { Say ("llama runtime dlls: " + (($ls.Modules | Where-Object { $_.ModuleName -match '^(msvcp140|vcruntime140|vcruntime140_1)\.dll$' } | ForEach-Object { $_.FileName }) -join ' | ')) }
    $lt.GetMethod('Stop', $SF).Invoke($null, @())
    Say "DONE"
} catch {
    Say ("EXCEPTION: " + $_.Exception.ToString())
}
