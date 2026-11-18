# Subtext on a CLEAN Windows (WORKPLAN 2lamed-dalet, 18.11.2026) - Binyamin: "the tests on a clean machine should be
# thorough, because in every earlier stage we skipped it". A Hyper-V machine from D:\Claude\vm (read its README first),
# restored to its clean checkpoint: the installer, the first run, the test packages, the local models for real, removal.
#
#   build\test-clean-vm.ps1 -VMName winapps-w11 -Checkpoint clean-hebrew-user
#       [-Stages pre,copy,install,run,suites,local,uninstall] [-Packages logic,subs,...] [-Net] [-Cpu 2] [-MemGB 4]
#       [-User TESTPC\tester] [-Keep]
#
# -Net adds a network adapter on "Default Switch" (the machines have none) - needed for the stage "local" (real GitHub).
# -Cpu/-MemGB make a weak machine (the machine is turned off for it, and boots cold); -CpuLimit N caps its processors at N
# percent while it runs (keeps the logged-on session). Restoring the checkpoint afterwards puts the old settings back.
# The report and the screenshots go to %TEMP%\subtext-vm-<machine>-<time>. ASCII only: PowerShell 5.1 reads a file
# without a BOM as the ANSI code page. The Hebrew user name comes from vmtools (Get-BenchHebrewName).
param(
    [Parameter(Mandatory = $true)][string]$VMName,
    [string]$Checkpoint = '',
    [string[]]$Stages = @('pre', 'copy', 'install', 'run', 'suites', 'local', 'uninstall'),
    [string[]]$Packages = @(),
    [switch]$Net,
    [int]$Cpu = 0,
    [int]$MemGB = 0,
    [int]$CpuLimit = 0,
    [switch]$Heavy,
    [string]$GuestCommand = '',
    [string]$User = '',
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
# "powershell -File" passes "a,b,c" as ONE string - split it (the first run skipped every stage that way)
$Stages = @($Stages | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Packages = @($Packages | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
. D:\Claude\vm\vmtools.ps1
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $env:TEMP ('subtext-vm-' + $VMName + '-' + (Get-Date -Format 'MMdd-HHmm'))
New-Item -ItemType Directory -Force $out | Out-Null
function Log([string]$s) {
    $line = (Get-Date -Format 'HH:mm:ss') + '  ' + $s
    Write-Host $line
    [IO.File]::AppendAllText((Join-Path $out 'log.txt'), $line + "`r`n", (New-Object Text.UTF8Encoding($true)))
}
$cred = if ($User) { New-Object Management.Automation.PSCredential($User, (ConvertTo-SecureString 'Test1234!' -AsPlainText -Force)) } else { Get-BenchCred }
$userName = $cred.UserName

# ---- the machine ----
if ($Checkpoint) {
    if ((Get-VM $VMName).State -ne 'Off') { Stop-VM $VMName -TurnOff -Force }
    Restore-VMSnapshot -VMName $VMName -Name $Checkpoint -Confirm:$false
    Log "restored $Checkpoint"
}
if ($Cpu -gt 0 -or $MemGB -gt 0) {
    if ((Get-VM $VMName).State -ne 'Off') { Stop-VM $VMName -TurnOff -Force }
    if ($Cpu -gt 0) { Set-VMProcessor -VMName $VMName -Count $Cpu }
    if ($MemGB -gt 0) { Set-VMMemory -VMName $VMName -DynamicMemoryEnabled $false -StartupBytes ([int64]$MemGB * 1GB) }
    Log "weak machine: cpu $Cpu, memory $MemGB GB"
}
if ((Get-VM $VMName).State -ne 'Running') { Start-VM $VMName; Log "started" }
if ($CpuLimit -gt 0) {
    try { Set-VMProcessor -VMName $VMName -Maximum $CpuLimit; Log ("processors capped at $CpuLimit% (" + (Get-VMProcessor $VMName).Count + " of them)") }
    catch { Log ("could not cap the processors: " + $_.Exception.Message) }
}
$s = $null
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $s -and $sw.Elapsed.TotalMinutes -lt 6) {
    try { $s = New-PSSession -VMName $VMName -Credential $cred -ErrorAction Stop } catch { Start-Sleep -Seconds 5 }
}
if (-not $s) { throw "no PowerShell Direct session after 6 minutes" }
Log ("session as " + $userName + " after {0:N0}s" -f $sw.Elapsed.TotalSeconds)
if ($Net) {
    if (-not (Get-VMNetworkAdapter -VMName $VMName)) { Add-VMNetworkAdapter -VMName $VMName -SwitchName 'Default Switch'; Log "network adapter added (Default Switch)" }
    $ok = $false
    for ($i = 0; $i -lt 30 -and -not $ok; $i++) { $ok = Invoke-Command -Session $s { try { (Invoke-WebRequest 'https://api.github.com' -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200 } catch { $false } }; if (-not $ok) { Start-Sleep -Seconds 4 } }
    Log ("network: " + $ok)
}

# In the user's desktop session (a GUI started straight from PowerShell Direct is invisible), not elevated, and wait.
function Desktop([string]$Exe, [string]$Arguments = '', [int]$WaitSeconds = 0) {
    Invoke-Command -Session $s -ScriptBlock {
        param($exe, $arg, $wait, $user)
        $a = if ($arg) { New-ScheduledTaskAction -Execute $exe -Argument $arg } else { New-ScheduledTaskAction -Execute $exe }
        # NOT elevated - like a user double-clicking it (elevated, test-errors could write into C:\Windows and its
        # "protected folder" check failed)
        $p = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        $name = 'bench-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $st = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
        Register-ScheduledTask -TaskName $name -Action $a -Principal $p -Settings $st | Out-Null
        Start-ScheduledTask -TaskName $name
        $state = 'Running'; $note = ''
        if ($wait -gt 0) {
            $w = [Diagnostics.Stopwatch]::StartNew()
            # The scheduler can fail a query now and then (18.11, 8 minutes into test-all: "The system cannot find the
            # file specified" - and the whole run stopped). One failed query is not the end: keep asking, and only after
            # a minute of failures in a row wait for the process itself, found by its command line
            $miss = 0
            do {
                Start-Sleep -Milliseconds 500
                try { $state = [string](Get-ScheduledTask -TaskName $name -ErrorAction Stop).State; $miss = 0 }
                catch { $miss++; if ($miss -eq 1) { $note = $_.Exception.Message }; if ($miss -ge 120) { $state = 'Lost' } }
            } while ($state -eq 'Running' -and $w.Elapsed.TotalSeconds -lt $wait)
            if ($state -eq 'Lost') {
                $key = if ($arg) { $arg } else { $exe }
                while ($w.Elapsed.TotalSeconds -lt $wait -and @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($key) }).Count -gt 0) { Start-Sleep -Seconds 2 }
            }
        } else { Start-Sleep -Seconds 2 }
        $last = $null
        try { $last = (Get-ScheduledTaskInfo -TaskName $name -ErrorAction Stop).LastTaskResult } catch { }
        try { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction Stop } catch { }
        [pscustomobject]@{ LastResult = $last; State = [string]$state; Note = $note }
    } -ArgumentList $Exe, $Arguments, $WaitSeconds, $userName
}
function Shot([string]$name) { Save-BenchScreen -VMName $VMName -Path (Join-Path $out ($name + '.png')) | Out-Null; Log "screenshot $name.png" }

$guest = 'C:\bench'
# the installer carries the app inside it: one older than dist\Subtext.exe tests an old app (18.11: the accurate model
# "downloaded" in 0 s - the setup had been built before it was in the catalog)
if ($Stages -contains 'install' -and (Get-Item (Join-Path $root 'dist\Subtext-Setup.exe')).LastWriteTime -lt (Get-Item (Join-Path $root 'dist\Subtext.exe')).LastWriteTime) {
    throw 'dist\Subtext-Setup.exe is older than dist\Subtext.exe - run build\make-installer.cmd first'
}
try {
    if ($Stages -contains 'pre') {
        $pre = Invoke-Command -Session $s {
            $os = Get-CimInstance Win32_OperatingSystem
            $voices = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens' -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName })
            [ordered]@{
                os = $os.Caption + ' ' + $os.Version + ' (' + $os.OSArchitecture + ')'; user = $env:USERNAME; profile = $env:USERPROFILE; temp = $env:TEMP
                net48 = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release
                msvcp140 = Test-Path "$env:WINDIR\System32\msvcp140.dll"; vcruntime140 = Test-Path "$env:WINDIR\System32\vcruntime140.dll"
                vcredist = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like '*Visual C++*' } | ForEach-Object { $_.DisplayName }) -join '; '
                cpus = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors; memGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
                freeC = [math]::Round((Get-PSDrive C).Free / 1GB, 1); culture = (Get-Culture).Name; uiLang = (Get-UICulture).Name; voices = $voices -join ', '
                python = [bool](Get-Command python -ErrorAction SilentlyContinue)
            }
        }
        foreach ($k in $pre.Keys) { Log ("pre " + $k + ': ' + $pre[$k]) }
    }

    if ($Stages -contains 'copy') {
        # what the tests need: build\ (scripts, lang, payload), dist\ (the EXE and the setup), src\ (test-source), root files
        $stage = Join-Path $out 'bench'
        New-Item -ItemType Directory -Force (Join-Path $stage 'repo'), (Join-Path $stage 'data') | Out-Null
        foreach ($d in @('build', 'dist', 'src', 'docs')) { Copy-Item (Join-Path $root $d) (Join-Path $stage "repo\$d") -Recurse -Force }
        # test-release compares the packed engine with its source - a build tool, not something the app needs
        New-Item -ItemType Directory -Force (Join-Path $stage 'repo\tools') | Out-Null
        Copy-Item (Join-Path $root 'tools\ffmpeg.exe') (Join-Path $stage 'repo\tools\ffmpeg.exe')
        Get-ChildItem $root -File | Where-Object { $_.Extension -in '.json', '.md', '.cmd' } | Copy-Item -Destination (Join-Path $stage 'repo') -Force
        Copy-Item (Join-Path $root 'dist\Subtext-Setup.exe') $stage
        Copy-Item 'D:\Claude\_research\local-models\data\kan-90.wav' (Join-Path $stage 'data')
        Copy-Item 'D:\Claude\vivobook-hebrew-subs\cues_en.json' (Join-Path $stage 'data')
        $mb = [math]::Round((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
        $sw.Restart()
        Invoke-Command -Session $s { param($g) if (Test-Path $g) { Remove-Item $g -Recurse -Force }; New-Item -ItemType Directory $g | Out-Null } -ArgumentList $guest
        Copy-Item (Join-Path $stage '*') -Destination $guest -ToSession $s -Recurse -Force
        Log ("copied $mb MB to the guest in {0:N0}s" -f $sw.Elapsed.TotalSeconds)
    }

    if ($Stages -contains 'install') {
        $r = Desktop "$guest\Subtext-Setup.exe" '/S' 180
        $inst = Invoke-Command -Session $s {
            $u = Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath } | Where-Object { $_.DisplayName -like 'Subtext*' } | Select-Object -First 1
            $dir = if ($u) { $u.InstallLocation } else { '' }
            $lnk = @(Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs", ([Environment]::GetFolderPath('Desktop')) -Filter 'Subtext*.lnk' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
            $ow = Test-Path 'HKCU:\Software\Classes\.srt\OpenWithProgids'
            $setupLog = @(Get-ChildItem $env:TEMP -Filter '*Subtext*setup*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { Get-Content $_.FullName -Tail 5 }) -join ' | '
            [ordered]@{ uninstallKey = [bool]$u; name = $(if ($u) { $u.DisplayName + ' ' + $u.DisplayVersion } else { '' }); dir = $dir; exe = $(if ($dir) { Test-Path (Join-Path $dir 'Subtext.exe') } else { $false })
                        shortcuts = $lnk -join '; '; openWithSrt = $ow; setupLog = $setupLog }
        }
        Log ("install: task result " + $r.LastResult + ", state " + $r.State)
        foreach ($k in $inst.Keys) { Log ("install " + $k + ': ' + $inst[$k]) }
        $script:installDir = $inst.dir
    }

    if ($Stages -contains 'run') {
        $exe = if ($script:installDir) { Join-Path $script:installDir 'Subtext.exe' } else { "$guest\repo\dist\Subtext.exe" }
        $sw.Restart()
        [void](Desktop $exe '' 0)
        $ready = $false
        for ($i = 0; $i -lt 40 -and -not $ready; $i++) {
            Start-Sleep -Seconds 3
            # MainWindowTitle is empty from PowerShell Direct (another session's desktop) - the process and the unpacked engine
            # say it is up; the screenshot shows the window
            $ready = Invoke-Command -Session $s { [bool](Get-Process Subtext -ErrorAction SilentlyContinue) -and [bool](Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SubtitleStudio\runtime') -Recurse -Filter ffmpeg.stamp -ErrorAction SilentlyContinue) }
        }
        Log ("first run: process up and engine unpacked " + $ready + " after {0:N0}s" -f $sw.Elapsed.TotalSeconds)
        Start-Sleep -Seconds 3
        Shot '1-first-run'
        $rt = Invoke-Command -Session $s {
            $d = Join-Path $env:LOCALAPPDATA 'SubtitleStudio\runtime'
            [ordered]@{ runtime = @(Get-ChildItem $d -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName + ' ' + [math]::Round($_.Length / 1MB) + 'MB' }) -join '; '
                        title = @(Get-Process Subtext -ErrorAction SilentlyContinue | ForEach-Object { $_.MainWindowTitle }) -join ' | '
                        werReports = @(Get-ChildItem "$env:LOCALAPPDATA\Microsoft\Windows\WER\ReportArchive", "$env:ProgramData\Microsoft\Windows\WER\ReportArchive" -Filter '*Subtext*' -ErrorAction SilentlyContinue).Count }
        }
        foreach ($k in $rt.Keys) { Log ("run " + $k + ': ' + $rt[$k]) }
        Invoke-Command -Session $s { Get-Process Subtext -ErrorAction SilentlyContinue | Stop-Process -Force }
    }

    if ($Stages -contains 'suites') {
        $only = if ($Packages.Count -gt 0) { ' -Only ' + ($Packages -join ',') } else { '' }
        $cmd = "-NoProfile -ExecutionPolicy Bypass -Command `"& '$guest\repo\build\test-all.ps1'$only -AllowSkip *> '$guest\test-all.txt'`""
        $sw.Restart()
        $r = Desktop 'powershell.exe' $cmd 7200
        Log (("suites: task result " + $r.LastResult + ", state " + $r.State + " after {0:N0} min" -f $sw.Elapsed.TotalMinutes) + $(if ($r.Note) { " (the scheduler once said: " + $r.Note + ")" } else { '' }))
        Copy-Item "$guest\test-all.txt" (Join-Path $out 'test-all.txt') -FromSession $s -ErrorAction SilentlyContinue
        # each failing package's whole output (test-all writes it to TEMP) - restoring the checkpoint deletes it
        $fails = Invoke-Command -Session $s { @(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Temp') -Filter 'ss-testall-fail-*.txt' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }) }
        foreach ($f in $fails) { Copy-Item $f $out -FromSession $s -ErrorAction SilentlyContinue }
        Log ("failure reports copied: " + @($fails).Count)
        if (Test-Path (Join-Path $out 'test-all.txt')) { Get-Content (Join-Path $out 'test-all.txt') -Tail 45 | ForEach-Object { Log ("  " + $_) } }
    }

    # -Stages cmd -GuestCommand "...": any PowerShell line in the user's desktop session, from the copied repo, its output kept
    if ($Stages -contains 'cmd' -and $GuestCommand) {
        $cmd = "-NoProfile -ExecutionPolicy Bypass -Command `"[Console]::OutputEncoding = [Text.Encoding]::UTF8; Set-Location '$guest\repo'; & { $GuestCommand } *> '$guest\cmd.txt'`""
        $r = Desktop 'powershell.exe' $cmd 3600
        Copy-Item "$guest\cmd.txt" (Join-Path $out 'cmd.txt') -FromSession $s -ErrorAction SilentlyContinue
        Log ("cmd: task result " + $r.LastResult)
        if (Test-Path (Join-Path $out 'cmd.txt')) { Get-Content (Join-Path $out 'cmd.txt') -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*ok ' } | ForEach-Object { Log ("  " + $_) } }
    }

    if ($Stages -contains 'local') {
        $exe = if ($script:installDir) { Join-Path $script:installDir 'Subtext.exe' } else { "$guest\repo\dist\Subtext.exe" }
        Copy-Item (Join-Path $PSScriptRoot 'vm\guest-local.ps1') "$guest\guest-local.ps1" -ToSession $s -Force
        $cmd = "-NoProfile -ExecutionPolicy Bypass -File `"$guest\guest-local.ps1`" -Exe `"$exe`" -Wav `"$guest\data\kan-90.wav`" -Cues `"$guest\data\cues_en.json`" -Out `"$guest\local.txt`"" + $(if ($Heavy) { ' -Heavy' } else { '' })
        $sw.Restart()
        $r = Desktop 'powershell.exe' $cmd 3600
        Log (("local: task result " + $r.LastResult + ", state " + $r.State + " after {0:N0} min" -f $sw.Elapsed.TotalMinutes) + $(if ($r.Note) { " (the scheduler once said: " + $r.Note + ")" } else { '' }))
        Copy-Item "$guest\local.txt" (Join-Path $out 'local.txt') -FromSession $s -ErrorAction SilentlyContinue
        if (Test-Path (Join-Path $out 'local.txt')) { Get-Content (Join-Path $out 'local.txt') -Encoding UTF8 | ForEach-Object { Log ("  " + $_) } }
    }

    if ($Stages -contains 'uninstall' -and $script:installDir) {
        $r = Desktop "$guest\Subtext-Setup.exe" '/uninstall /S' 180
        $left = Invoke-Command -Session $s {
            param($dir)
            [ordered]@{ dirLeft = Test-Path $dir; uninstallKey = [bool](Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath } | Where-Object { $_.DisplayName -like 'Subtext*' })
                        shortcuts = @(Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs", ([Environment]::GetFolderPath('Desktop')) -Filter 'Subtext*.lnk' -Recurse -ErrorAction SilentlyContinue).Count
                        openWithSrt = [bool](Get-ItemProperty 'HKCU:\Software\Classes\.srt\OpenWithProgids' -ErrorAction SilentlyContinue | Get-Member -Name 'SubtitleStudio*' -ErrorAction SilentlyContinue)
                        settingsKept = Test-Path (Join-Path $env:APPDATA 'SubtitleStudio') }
        } -ArgumentList $script:installDir
        Log ("uninstall: task result " + $r.LastResult)
        foreach ($k in $left.Keys) { Log ("uninstall " + $k + ': ' + $left[$k]) }
    }
} finally {
    if ($s) { Remove-PSSession $s }
    if (-not $Keep) {
        Stop-VM $VMName -TurnOff -Force
        if ($Checkpoint) { Restore-VMSnapshot -VMName $VMName -Name $Checkpoint -Confirm:$false; Log "restored $Checkpoint and turned off" }
        # after the restore the adapter may already be gone with the checkpoint - not an error
        if ($Net) { try { Get-VMNetworkAdapter -VMName $VMName -ErrorAction Stop | Remove-VMNetworkAdapter -ErrorAction Stop } catch { } }
    }
    Log "report: $out"
}
