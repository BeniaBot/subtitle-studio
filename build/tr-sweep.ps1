# tr-sweep.ps1 - תמלול אמיתי של כמה קבצים, בדיוק כמו התוכנה (Transcribe.Run עם
# המפתח השמור), ומדידה של מה שמשתמש היה מרגיש: האם כל הקובץ תומלל, חורים,
# חפיפות, כתוביות קצרות מדי או מהירות מדי לקריאה, כפילויות, שורה ארוכה.
#
# **צורך מכסה:** בערך בקשה אחת לכל דקת שמע אצל גוגל. ההגדרות רק נקראות.
#   tr-sweep.ps1 -List files.txt      הפלט: %TEMP%\ss-sweep\tr-<n>.srt ודוח בחלון
param([Parameter(Mandatory = $true)][string]$List, [string]$Out = '')
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions, System.Security
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
[void](TY 'Settings').GetMethod('Load', $ST).Invoke($null, @())
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
if (-not $Out) { $Out = Join-Path $env:TEMP 'ss-sweep' }
New-Item -ItemType Directory -Force $Out | Out-Null
$run = (TY 'Transcribe').GetMethods($ST) | Where-Object { $_.Name -eq 'Run' -and $_.GetParameters().Count -eq 5 }

$n = 0
foreach ($f in @([IO.File]::ReadAllLines($List, [Text.Encoding]::UTF8) | Where-Object { $_.Trim() -and -not $_.StartsWith('#') })) {
    $n++
    Write-Host ''
    Write-Host ("[{0}] {1}" -f $n, $f) -ForegroundColor Cyan
    $mi = (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$f))
    $dur = $mi.DurationSec
    $argv = New-Object object[] 5; $argv[0] = [string]$f; $argv[1] = [long]$mi.DurationMs; $argv[2] = ''
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $res = $run.Invoke($null, $argv)
    $cues = @($res.Cues | Sort-Object Start)
    Write-Host ('  {0:0}s  chunks={1} failed={2} quota={3} error="{4}" gaps="{5}"  duration {6:0.0}s  recovered={7}' -f $sw.Elapsed.TotalSeconds, $res.Chunks, $res.Failed, $res.QuotaOut, $res.Error, ($res.Gaps -join ', '), $dur, $res.Recovered)
    if ($cues.Count -eq 0) { Write-Host '  NO CUES' -ForegroundColor Red; continue }

    $first = $cues[0].Start / 1000.0; $lastEnd = ($cues | ForEach-Object { $_.End } | Measure-Object -Maximum).Maximum / 1000.0
    $maxGap = 0.0; $gapAt = 0.0; $over = 0; $short = 0; $fast = 0; $long = 0; $dup = 0; $empty = 0; $wide = 0; $untimed = 0; $heb = 0; $lat = 0
    for ($i = 0; $i -lt $cues.Count; $i++) {
        $c = $cues[$i]; $s = $c.Start / 1000.0; $e = $c.End / 1000.0; $d = $e - $s
        $txt = [string]$c.Text
        if ($i -gt 0) {
            $pe = $cues[$i - 1].End / 1000.0
            if ($s - $pe -gt $maxGap) { $maxGap = $s - $pe; $gapAt = $pe }
            if ($s -lt $pe - 0.05) { $over++ }
            if ($txt.Trim() -eq ([string]$cues[$i - 1].Text).Trim()) { $dup++ }
        }
        if ($d -lt 0.7) { $short++ }
        if ($d -gt 7.5) { $long++ }
        if (-not $txt.Trim()) { $empty++ }
        $chars = ($txt -replace '\s', '').Length
        if ($d -gt 0 -and $chars / $d -gt 20) { $fast++ }
        foreach ($ln in ($txt -split "`n")) { if ($ln.Length -gt 42) { $wide++ } }
        if ($c.Untimed) { $untimed++ }
        foreach ($ch in $txt.ToCharArray()) { if ($ch -ge [char]0x05D0 -and $ch -le [char]0x05EA) { $heb++ } elseif (($ch -ge 'a' -and $ch -le 'z') -or ($ch -ge 'A' -and $ch -le 'Z')) { $lat++ } }
    }
    $lead = $first; $tail = $dur - $lastEnd
    Write-Host ('  cues {0}  first {1:0.0}s  last end {2:0.0}s (of {3:0.0})  biggest gap {4:0.0}s at {5:0.0}s' -f $cues.Count, $first, $lastEnd, $dur, $maxGap, $gapAt)
    Write-Host ('  overlaps {0}  <0.7s {1}  >7.5s {2}  >20cps {3}  dup-in-a-row {4}  empty {5}  line>42 {6}  untimed {7}  letters he/lat {8}/{9}' -f $over, $short, $long, $fast, $dup, $empty, $wide, $untimed, $heb, $lat)
    $sb = New-Object Text.StringBuilder; $k = 0
    foreach ($c in $cues) {
        $k++
        [void]$sb.AppendLine($k); [void]$sb.AppendLine(('{0:hh\:mm\:ss\,fff} --> {1:hh\:mm\:ss\,fff}' -f [TimeSpan]::FromMilliseconds($c.Start), [TimeSpan]::FromMilliseconds($c.End))); [void]$sb.AppendLine($c.Text); [void]$sb.AppendLine('')
        Write-Host ('    {0,6:0.0}-{1,6:0.0} {2}{3}' -f ($c.Start / 1000.0), ($c.End / 1000.0), $(if ($c.Untimed) { '~' } else { ' ' }), ($c.Text -replace "`r?`n", ' / '))
    }
    [IO.File]::WriteAllText((Join-Path $Out ("tr-$n.srt")), $sb.ToString(), (New-Object Text.UTF8Encoding $true))
}
