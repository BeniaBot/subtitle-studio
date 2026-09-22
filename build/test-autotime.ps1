# תזמון אוטומטי לפי הדיבור - נגד זמנים ידועים.
#
# שני חלקים. **סינתטי**: פס קול בנוי ביד, שבו יודעים בדיוק איפה כל
# משפט מתחיל - בודק את ההחלטות עצמן (סף, שתיקות, סדר, עוגנים).
# **דיבור אמיתי**: קול מסונתז של ווינדוס, משפט-משפט, עם שתיקות באורך
# ידוע ביניהם - בודק שזה עובד על אנרגיה של דיבור ולא רק על מלבנים.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist/Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
# ‏New-Object byte[] מחזיר PSObject עוטף, ו-Invoke לא יודע להמיר אותו
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$at = T 'AutoTime'; $cueT = T 'Cue'
$listT = [System.Collections.Generic.List``1].MakeGenericType($cueT)
$ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
$fS = $cueT.GetField('Start'); $fE = $cueT.GetField('End'); $fU = $cueT.GetField('Untimed')
$run = $at.GetMethod('Run', $ST)
$P = 10   # Waveform.PeriodMs

function NewCues($texts, $untimed) {
    $l = [Activator]::CreateInstance($listT)
    $t = 0
    foreach ($x in $texts) {
        $c = $ctor.Invoke(@([long]$t, [long]($t + 1000), [string]$x))
        $fU.SetValue($c, [bool]$untimed)
        [void]$listT.GetMethod('Add').Invoke($l, (Pack $c))
        $t += 1100
    }
    return ,$l
}

# פס קול: רשימת [התחלה, סוף] של דיבור במ״ש, רצפת רעש ועוצמת דיבור
function Track($speech, $totalMs, $floor, $level) {
    $n = [int]($totalMs / $P) + 8
    $a = New-Object byte[] $n
    for ($i = 0; $i -lt $n; $i++) { $a[$i] = [byte]$floor }
    foreach ($s in $speech) {
        for ($i = [int]($s[0]/$P); $i -lt [int]($s[1]/$P); $i++) { $a[$i] = [byte]$level }
    }
    return ,$a
}

function Starts($l) { $r = @(); foreach ($c in $l) { $r += $fS.GetValue($c) }; return ,$r }
function Timed($res) { return $res.GetType().GetField('Timed').GetValue($res) }

# ================= סינתטי =================
Write-Host 'סף מותאם להקלטה'
$trk = Track @(@(1000,3000),@(4000,7000)) 9000 3 60
$th = $at.GetMethod('Threshold', $ST).Invoke($null, (Pack $trk 0 ([int]$trk.Length)))
Check 'הסף בין הרעש לדיבור' (($th -gt 3) -and ($th -lt 60)) "סף=$th"
$trkN = Track @(@(1000,3000),@(4000,7000)) 9000 25 70
$thN = $at.GetMethod('Threshold', $ST).Invoke($null, (Pack $trkN 0 ([int]$trkN.Length)))
Check 'הקלטה רועשת: הסף מעל רצפת הרעש' ($thN -gt 25) "סף=$thN"

Write-Host ''
Write-Host 'ארבעה משפטים, ארבעה גושי דיבור'
$blocks = @(@(500,2500),@(3100,6100),@(6800,7800),@(8400,12400))
$texts  = @('aaaaaaaaaaaaaaaaaaaa','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','aaaaaaaaaa','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')
$trk = Track $blocks 13500 3 60
$cues = NewCues $texts $true
$res = $run.Invoke($null, (Pack $cues $trk $trk ([long]13500) $false))
Check 'כולן תוזמנו' ((Timed $res) -eq 4) ("תוזמנו=" + (Timed $res))
$worst = 0
for ($k = 0; $k -lt 4; $k++) {
    $err = [Math]::Abs(($fS.GetValue($cues[$k]) + 120) - $blocks[$k][0])
    if ($err -gt $worst) { $worst = $err }
}
Check 'כל התחלה בתוך 150ms מתחילת הדיבור' ($worst -le 150) "סטייה מרבית=${worst}ms"
$ok = $true
for ($k = 0; $k -lt 3; $k++) { if ($fE.GetValue($cues[$k]) -gt $fS.GetValue($cues[$k+1])) { $ok = $false } }
Check 'אין חפיפות' $ok ''
$none = $true
foreach ($c in $cues) { if ($fU.GetValue($c)) { $none = $false } }
Check 'הדגל ״לא מתוזמן״ ירד' $none ''

Write-Host ''
Write-Host 'טקסט שלא פרופורציוני לדיבור'
# אותם גושים, אבל השורה השנייה קצרה בטקסט - הגבולות עדיין חייבים ליפול בשתיקות
$cuesU = NewCues @('aaaaaaaaaaaaaaaaaaaa','aaaaaaaaaaaaaaa','aaaaaaaaaaaaaaa','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') $true
[void]$run.Invoke($null, (Pack $cuesU $trk $trk ([long]13500) $false))
$worstU = 0
for ($k = 0; $k -lt 4; $k++) {
    $err = [Math]::Abs(($fS.GetValue($cuesU[$k]) + 120) - $blocks[$k][0])
    if ($err -gt $worstU) { $worstU = $err }
}
Check 'השתיקות מושכות את הגבולות גם כשהטקסט לא מדויק' ($worstU -le 150) "סטייה מרבית=${worstU}ms"

Write-Host ''
Write-Host 'נשימה באמצע משפט'
$trk2 = Track @(@(500,1400),@(1500,2500),@(3100,6100)) 7000 3 60
$cues2 = NewCues @('aaaaaaaaaaaaaaaaaaaa','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') $true
[void]$run.Invoke($null, (Pack $cues2 $trk2 $trk2 ([long]7000) $false))
$e2 = [Math]::Abs(($fS.GetValue($cues2[1]) + 120) - 3100)
Check 'השורה השנייה לא נחתכה בנשימה' ($e2 -le 150) "התחלה=$($fS.GetValue($cues2[1]))"

Write-Host ''
Write-Host 'פחות שתיקות מגבולות'
$trk3 = Track @(@(500,4500),@(5200,9200)) 10000 3 60
$cues3 = NewCues @('aaaaaaaaaa','aaaaaaaaaa','aaaaaaaaaaaaaaaaaaaa') $true
[void]$run.Invoke($null, (Pack $cues3 $trk3 $trk3 ([long]10000) $false))
$ok3 = $true
for ($k = 0; $k -lt 2; $k++) {
    if ($fE.GetValue($cues3[$k]) -gt $fS.GetValue($cues3[$k+1])) { $ok3 = $false }
    if ($fS.GetValue($cues3[$k]) -ge $fS.GetValue($cues3[$k+1])) { $ok3 = $false }
}
Check 'הסדר נשמר ואין חפיפה' $ok3 ("התחלות=" + ((Starts $cues3) -join ','))
$e3 = [Math]::Abs(($fS.GetValue($cues3[2]) + 120) - 5200)
Check 'השתיקה היחידה נוצלה לגבול הנכון' ($e3 -le 150) "התחלה שלישית=$($fS.GetValue($cues3[2]))"

Write-Host ''
Write-Host 'עוגן באמצע'
$trk4 = Track @(@(500,2500),@(3100,5100),@(5800,7800)) 9000 3 60
$cues4 = NewCues @('aaaaaaaaaa','aaaaaaaaaa','aaaaaaaaaa') $true
$anchor = $cues4[1]
$fS.SetValue($anchor, [long]3000); $fE.SetValue($anchor, [long]5300); $fU.SetValue($anchor, $false)
[void]$run.Invoke($null, (Pack $cues4 $trk4 $trk4 ([long]9000) $false))
Check 'העוגן לא זז' (($fS.GetValue($anchor) -eq 3000) -and ($fE.GetValue($anchor) -eq 5300)) ''
Check 'השורה שלפניו לא חורגת לתוכו' ($fE.GetValue($cues4[0]) -le 3000) "סוף=$($fE.GetValue($cues4[0]))"
Check 'השורה שאחריו מתחילה אחריו' ($fS.GetValue($cues4[2]) -ge 5300) "התחלה=$($fS.GetValue($cues4[2]))"

Write-Host ''
Write-Host 'הקלטה שקטה מאוד'
$quietR = Track @(@(1000,3000),@(3800,6000)) 7000 0 2
$quietP = Track @(@(1000,3000),@(3800,6000)) 7000 1 22
$pick = $at.GetMethod('PickChannel', $ST).Invoke($null, (Pack $quietR $quietP ([long]7000)))
Check 'נופלים לשיא כשה-RMS דחוס' ([object]::ReferenceEquals($pick, $quietP)) ''
$cues5 = NewCues @('aaaaaaaaaaaaaaaaaaaa','aaaaaaaaaaaaaaaaaaaaaa') $true
[void]$run.Invoke($null, (Pack $cues5 $quietR $quietP ([long]7000) $false))
$e5 = [Math]::Abs(($fS.GetValue($cues5[1]) + 120) - 3800)
Check 'ועדיין מוצאים את הגבול' ($e5 -le 150) "התחלה=$($fS.GetValue($cues5[1]))"

Write-Host ''
Write-Host 'מקרי קצה'
$empty = [Activator]::CreateInstance($listT)
$re = $run.Invoke($null, (Pack $empty $trk $trk ([long]13500) $false))
Check 'רשימה ריקה - שגיאה ולא קריסה' ($re.GetType().GetField('Error').GetValue($re) -ne $null) ''
$rn = $run.Invoke($null, (Pack (NewCues @('a') $true) $null $null ([long]1000) $false))
Check 'בלי פס קול - שגיאה ולא קריסה' ($rn.GetType().GetField('Error').GetValue($rn) -ne $null) ''
$silent = Track @() 8000 3 3
$rs = $run.Invoke($null, (Pack (NewCues @('aaaa','bbbb') $true) $silent $silent ([long]8000) $false))
Check 'שקט מוחלט - לא ״מתזמן״ לתוך כלום' ((Timed $rs) -eq 0) ("תוזמנו=" + (Timed $rs))

# ================= דיבור אמיתי =================
Write-Host ''
Write-Host 'דיבור אמיתי (קול מסונתז של ווינדוס)'
$haveSpeech = $true
try { Add-Type -AssemblyName System.Speech } catch { $haveSpeech = $false }
if (-not $haveSpeech) { Write-Host '  (אין System.Speech - מדלג)' }
else {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    $sentences = @(
        'Welcome everyone to the weekly lesson.',
        'Today we will look at three short ideas.',
        'The first one is simple.',
        'The second one takes a little longer to explain properly.',
        'And the last one we will leave for next week.'
    )
    $gapsMs = @(700, 900, 600, 1100)
    $pcm = New-Object System.Collections.Generic.List[byte]
    $onsets = @()
    $tmp = Join-Path $env:TEMP 'autotime-s.raw'
    for ($k = 0; $k -lt $sentences.Count; $k++) {
        $fsOut = New-Object IO.FileStream($tmp, [IO.FileMode]::Create)
        $synth.SetOutputToAudioStream($fsOut, $fmt)
        $synth.Speak($sentences[$k])
        $synth.SetOutputToNull()
        $fsOut.Close()
        $bytes = [IO.File]::ReadAllBytes($tmp)
        # ההתחלה האמיתית: הדגימה הראשונה שחורגת מרעש, בתוך המשפט
        $first = 0
        for ($i = 0; $i + 1 -lt $bytes.Length; $i += 2) {
            $v = [BitConverter]::ToInt16($bytes, $i)
            if ([Math]::Abs([int]$v) -gt 900) { $first = $i / 2; break }
        }
        $baseMs = [int]($pcm.Count / 32)
        $onsets += $baseMs + [int]($first / 16)
        $pcm.AddRange($bytes)
        if ($k -lt $gapsMs.Count) { $pcm.AddRange((New-Object byte[] ($gapsMs[$k] * 32))) }
    }
    $pcm.AddRange((New-Object byte[] (800 * 32)))
    $synth.Dispose()

    $wav = Join-Path $env:TEMP 'autotime-real.wav'
    $data = $pcm.ToArray()
    $fsW = [IO.File]::Create($wav)
    $bw = New-Object IO.BinaryWriter($fsW)
    $bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $data.Length))
    $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1)
    $bw.Write([int]16000); $bw.Write([int]32000); $bw.Write([int16]2); $bw.Write([int16]16)
    $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]$data.Length); $bw.Write($data)
    $bw.Close()
    $totalMs = [long]($data.Length / 32)

    (T 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @()) | Out-Null
    $waveT = T 'Waveform'
    $w = [Activator]::CreateInstance($waveT)
    [void]$waveT.GetMethod('Build').Invoke($w, (Pack ([string]$wav) $totalMs))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $waveT.GetField('Ready').GetValue($w) -and $sw.ElapsedMilliseconds -lt 30000) { Start-Sleep -Milliseconds 50 }
    Check 'פס הקול נבנה מהקובץ' ($waveT.GetField('Ready').GetValue($w) -and -not $waveT.GetField('Failed').GetValue($w)) ''

    $cuesR = NewCues $sentences $true
    $rr = $run.Invoke($null, (Pack $cuesR $waveT.GetField('Rms').GetValue($w) $waveT.GetField('Peak').GetValue($w) $totalMs $false))
    Check 'כל המשפטים תוזמנו' ((Timed $rr) -eq $sentences.Count) ("תוזמנו=" + (Timed $rr))
    $worstR = 0; $line = ''
    for ($k = 0; $k -lt $sentences.Count; $k++) {
        $got = $fS.GetValue($cuesR[$k]) + 120
        $err = [Math]::Abs($got - $onsets[$k])
        if ($err -gt $worstR) { $worstR = $err }
        $line += "  [$k] $($onsets[$k])/$got"
    }
    Write-Host ("       אמיתי/נמצא:" + $line) -ForegroundColor DarkGray
    Check 'כל משפט אמיתי בתוך 300ms' ($worstR -le 300) "סטייה מרבית=${worstR}ms"
    Remove-Item $wav, $tmp -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
