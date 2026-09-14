# שיעור ארוך באמת: שלוש שעות, אלפי שורות. לא ״האם זה נכון״ - ״האם זה לא נתקע״.
#
# כל שאר הבדיקות רצות על קטעים של שניות. הקהל כאן מעלה שיעורים של שעה
# ושעתיים, ושם מתגלים דברים אחרים: מערך שגדל ריבועית, ציור שסורק את כל
# הכתוביות בכל פריים, JSON שנבנה בשרשור מחרוזות. מה נמדד:
#   1. פס הקול של 3 שעות (הדרך האמיתית: ffmpeg -> Waveform)
#   2. תזמון אוטומטי של ~2,900 שורות (תכנות דינמי, O(שורות x שתיקות) בזיכרון)
#   3. פרויקט עם 5,000 כתוביות: כתיבה, קריאה
#   4. החלון: טעינת 5,000 כתוביות, ציור הציר והרשימה
# התקציבים נדיבים בכוונה - המטרה לתפוס קפיצה פי 10, לא למדוד מילישניות.
# צפוי: 15 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\SubtitleStudio.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
$NP = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }
$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }
function Ms($sw) { return [int]$sw.Elapsed.TotalMilliseconds }

(T 'Runtime').GetMethod('Prepare', $SF).Invoke($null, @())
$ff = (T 'Ff').GetProperty('Exe', $SF).GetValue($null, $null)
$work = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'ss-long'))
New-Item -ItemType Directory $work -Force | Out-Null
$hours = 3
$durMs = [long]($hours * 3600 * 1000)
$audio = Join-Path $work ("lecture-{0}h.m4a" -f $hours)

# ---- הקלטה סינתטית: ״משפטים״ עם שתיקות לא סדירות ורעש רקע ----
if (-not (Test-Path $audio)) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $expr = '0.25*sin(2*PI*(180+40*sin(t))*t)*lt(mod(t\,3.7)\,3.0)*lt(mod(t\,11.3)\,10.4)+0.004*(random(0)-0.5)'
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $ff -hide_banner -loglevel error -y -f lavfi -i ("aevalsrc='" + $expr + "':s=8000:d=" + ($hours * 3600)) -c:a aac -b:a 32k $audio 2>&1 | Out-Null
    $ErrorActionPreference = $old
    Write-Host ("  (נוצרה הקלטה של $hours שעות ב-" + [int]$sw.Elapsed.TotalSeconds + " שניות)")
}
Check "הקלטה של $hours שעות קיימת" (Test-Path $audio) $audio

# ================= 1. פס הקול =================
Write-Host 'פס הקול'
$waveT = T 'Waveform'
$wave = [Activator]::CreateInstance($waveT)
$sw = [Diagnostics.Stopwatch]::StartNew()
[void]$waveT.GetMethod('Build').Invoke($wave, (Pack ([string]$audio) $durMs))
while (-not $wave.Ready -and $sw.Elapsed.TotalSeconds -lt 300) { Start-Sleep -Milliseconds 100 }
$waveMs = Ms $sw
Check 'פס הקול נבנה' ($wave.Ready -and -not $wave.Failed) ''
Check "תוך 90 שניות" ($waveMs -lt 90000) ("$waveMs ms")
$filled = 0; $rms = $wave.Rms; for ($i = $rms.Length - 2000; $i -lt $rms.Length - 1000; $i++) { if ($rms[$i] -gt 0) { $filled++ } }
Check 'הגיע עד הסוף (לא נעצר באמצע)' ($filled -gt 100) ("דליים עם קול בדקות האחרונות: $filled")

# ================= 2. תזמון אוטומטי =================
Write-Host 'תזמון אוטומטי'
$cueT = T 'Cue'
$ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
$listT = [Collections.Generic.List``1].MakeGenericType($cueT)
$cues = [Activator]::CreateInstance($listT)
$n = [int]($hours * 3600 / 3.7)
for ($k = 0; $k -lt $n; $k++) {
    $c = $ctor.Invoke(@([long]($k * 1100), [long]($k * 1100 + 1000), [string]('משפט מספר ' + $k + ' בשיעור הארוך')))
    $c.Untimed = $true
    [void]$listT.GetMethod('Add').Invoke($cues, (Pack $c))
}
[GC]::Collect()
$memBefore = [GC]::GetTotalMemory($true)
$sw = [Diagnostics.Stopwatch]::StartNew()
$res = (T 'AutoTime').GetMethod('Run', $SF).Invoke($null, (Pack $cues $wave.Rms $wave.Peak $durMs $false))
$atMs = Ms $sw
$peakMem = [GC]::GetTotalMemory($false) - $memBefore
Check "תוזמנו $n שורות" ($res.Timed -eq $n) ("timed=" + $res.Timed + " error=" + $res.Error)
Check 'תוך 15 שניות' ($atMs -lt 15000) ("$atMs ms, gaps=" + $res.Gaps)
$mono = $true; $prev = -1
foreach ($c in $cues) { if ($c.Start -lt $prev -or $c.End -le $c.Start) { $mono = $false; break }; $prev = $c.Start }
Check 'הסדר נשמר וכל שורה באורך חיובי' $mono ''
$last = $cues[$cues.Count - 1]
Check 'השורה האחרונה בסוף ההקלטה, לא נדחסה להתחלה' ($last.Start -gt $durMs * 0.95) ("last start=" + $last.Start)

# ================= 3. פרויקט גדול =================
Write-Host 'פרויקט עם 5,000 כתוביות'
$pdT = T 'ProjectData'; $prj = T 'Project'
$d = [Activator]::CreateInstance($pdT)
for ($k = 0; $k -lt 5000; $k++) { $d.Cues.Add($ctor.Invoke(@([long]($k * 2000), [long]($k * 2000 + 1800), [string]("שורה $k עם `"מרכאות`" ועוד טקסט כדי שיהיה אורך אמיתי")))) }
$pf = Join-Path $work 'big.subtext'
$sw = [Diagnostics.Stopwatch]::StartNew()
$json = $prj.GetMethod('ToJson', $SF).Invoke($null, (Pack $d))
[void]$prj.GetMethod('Write', $SF).Invoke($null, (Pack ([string]$pf) $json))
$wMs = Ms $sw
$sw = [Diagnostics.Stopwatch]::StartNew()
$a2 = New-Object object[] 2; $a2[0] = [string]$pf
$back = $prj.GetMethod('Load', $SF).Invoke($null, $a2)
$rMs = Ms $sw
Check 'נכתב ונקרא במלואו' ($back -ne $null -and $back.Cues.Count -eq 5000) $a2[1]
Check 'כתיבה תוך 2 שניות' ($wMs -lt 2000) ("$wMs ms, " + [int]((Get-Item $pf).Length / 1024) + " KB")
Check 'קריאה תוך 3 שניות' ($rMs -lt 3000) ("$rMs ms")

# ================= 4. החלון =================
Write-Host 'החלון עם 5,000 כתוביות'
$formT = T 'MainForm'
$f = [Activator]::CreateInstance($formT)
$f.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$f.Location = New-Object Drawing.Point -4000, -4000
$f.ClientSize = New-Object Drawing.Size 1493, 997
$f.Show(); [Windows.Forms.Application]::DoEvents()
$formT.GetMethod('OpenMedia', $NP, $null, [Type[]]@([string]), $null).Invoke($f, (Pack ([string]$audio))) | Out-Null
$doc = $formT.GetField('_doc', $NP).GetValue($f)
foreach ($c in $back.Cues) { $doc.Cues.Add($c) }
$sw = [Diagnostics.Stopwatch]::StartNew()
$formT.GetMethod('SyncAfterDocChange', $NP).Invoke($f, @()) | Out-Null
[Windows.Forms.Application]::DoEvents()
$syncMs = Ms $sw
Check 'טעינה לחלון תוך 2 שניות' ($syncMs -lt 2000) ("$syncMs ms")
function PaintMs($ctl) {
    $bmp = New-Object Drawing.Bitmap ([Math]::Max(1, $ctl.Width)), ([Math]::Max(1, $ctl.Height))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 5; $i++) { $ctl.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $ctl.Width, $ctl.Height)) }
    $ms = [int]($sw.Elapsed.TotalMilliseconds / 5)
    $bmp.Dispose()
    return $ms
}
$tl = $formT.GetField('_tl', $NP).GetValue($f)
$list = $formT.GetField('_list', $NP).GetValue($f)
$tlMs = PaintMs $tl
Check 'ציור ציר הזמן (כל השיעור במסך) תוך 250ms' ($tlMs -lt 250) ("$tlMs ms")
$tl.RestoreView(80.0, [long]($durMs / 2))
$tlMs2 = PaintMs $tl
Check 'ציור ציר הזמן בזום אמצע השיעור תוך 250ms' ($tlMs2 -lt 250) ("$tlMs2 ms")
$listMs = PaintMs $list
Check 'ציור רשימת הכתוביות תוך 250ms' ($listMs -lt 250) ("$listMs ms")
$f.Close(); $f.Dispose()
$wave.Abort()

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
