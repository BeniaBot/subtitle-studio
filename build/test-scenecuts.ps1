# מעברי סצנה - איתור והצמדה, נגד זמנים ידועים.
#
# ארבעה חלקים:
# 1. **קריאת היומן** של scdet ואיחוד חיתוכים צמודים.
# 2. **כללי ההצמדה** - מקרה-מקרה, כולל שני הבאגים שנתפסו בכתיבה:
#    סוף שהוארך נראה לכתובית הבאה כמו חפיפה, והארכה לתוך כתובית שלא זזה.
# 3. **מבחן אקראי**: מאות סידורים אקראיים. ההצמדה אסור שתיצור חפיפה,
#    תשנה סדר, או תזיז קצה יותר מהחלון - אף פעם.
# 4. **איתור אמיתי** בסרט סינתטי עם חיתוכים ידועים, דרך אותה עבודה שהתוכנה
#    מריצה; ואם יש במחשב את הצילומים של focus-video - גם על צילומים אמיתיים.
#
# צפוי: 39 בדיקות, ו-42 כשהצילומים האמיתיים קיימים. **פחות מזה = משהו דולג.**
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist/SubtitleStudio.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$sc = T 'SceneCuts'; $cueT = T 'Cue'
$listCueT = [System.Collections.Generic.List``1].MakeGenericType($cueT)
$listLongT = [System.Collections.Generic.List``1].MakeGenericType([long])
$ctor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))
$fS = $cueT.GetField('Start'); $fE = $cueT.GetField('End'); $fU = $cueT.GetField('Untimed')
$snap = $sc.GetMethod('Snap', $ST)
$norm = $sc.GetMethod('Normalize', $ST)

function Longs($arr) {
    $l = [Activator]::CreateInstance($listLongT)
    foreach ($x in $arr) { [void]$listLongT.GetMethod('Add').Invoke($l, (Pack ([long]$x))) }
    return ,$l
}
# ‏$pairs: מערך של @(התחלה, סוף) או @(התחלה, סוף, 'u') לשורה בלי תזמון
function Cues($pairs) {
    $l = [Activator]::CreateInstance($listCueT)
    foreach ($p in $pairs) {
        $c = $ctor.Invoke(@([long]$p[0], [long]$p[1], [string]'שורה'))
        if ($p.Count -gt 2) { $fU.SetValue($c, $true) }
        [void]$listCueT.GetMethod('Add').Invoke($l, (Pack $c))
    }
    return ,$l
}
function Times($l) { $s = @(); foreach ($c in $l) { $s += ('{0}-{1}' -f $fS.GetValue($c), $fE.GetValue($c)) }; return ($s -join ' ') }
function RunSnap($l, $cuts, $fps) { return $snap.Invoke($null, (Pack $l (Longs $cuts) ([double]$fps))) }
function St($l, $i) { return [long]$fS.GetValue($l[$i]) }
function En($l, $i) { return [long]$fE.GetValue($l[$i]) }

# ================= 1. קריאת היומן =================
Write-Host 'קריאת היומן'
$pl = $sc.GetMethod('ParseLine', $ST)
Check 'שורת חיתוך' ($pl.Invoke($null, @('[Parsed_scdet_0 @ 000001f9a9bceb80] lavfi.scd.score: 14.372, lavfi.scd.time: 5.333333')) -eq 5333) ''
Check 'שורת התקדמות - לא חיתוך' ($pl.Invoke($null, @('frame=  276 fps=0.0 q=-0.0 size=N/A time=00:00:09.20 bitrate=N/A speed=17.7x')) -eq -1) ''
Check 'זמן שלם בלי נקודה' ($pl.Invoke($null, @('[Parsed_scdet_0 @ 0] lavfi.scd.score: 12.063, lavfi.scd.time: 16')) -eq 16000) ''
$log = "frame=1 time=00:00:01.00`r[Parsed_scdet_0 @ 1] lavfi.scd.score: 20, lavfi.scd.time: 3`r`n" +
       "[Parsed_scdet_0 @ 1] lavfi.scd.score: 30, lavfi.scd.time: 7.5`nframe=2 time=00:00:08.00`r"
$parsed = $sc.GetMethod('Parse', $ST).Invoke($null, @($log))
Check 'יומן עם \r ו-\n מעורבבים' ((@($parsed) -join ',') -eq '3000,7500') (@($parsed) -join ',')
# ‏30fps: שלושה פריימים = 100ms. הבזק מייצר שני חיתוכים צמודים; 0 הוא לא חיתוך
$n = $norm.Invoke($null, (Pack (Longs @(9733, 5333, 5400, 0, 20700, 5333)) ([double]30)))
Check 'איחוד, מיון, והשמטת אפס' ((@($n) -join ',') -eq '5333,9733,20700') (@($n) -join ',')
$n = $norm.Invoke($null, (Pack (Longs @(5333, 5500)) ([double]30)))
Check 'חמישה פריימים - שני חיתוכים נפרדים' ((@($n) -join ',') -eq '5333,5500') (@($n) -join ',')

# ================= 2. כללי ההצמדה =================
# ‏30fps: חלון 12 פריימים = 400ms, ריווח 2 פריימים = 67ms
Write-Host 'כללי ההצמדה (30fps)'
Check 'חלון ב-30fps'  ($sc.GetMethod('WindowMs', $ST).Invoke($null, @([double]30)) -eq 400) ''
Check 'ריווח ב-30fps' ($sc.GetMethod('GapMs', $ST).Invoke($null, @([double]30)) -eq 67) ''
Check 'חלון ב-25fps'  ($sc.GetMethod('WindowMs', $ST).Invoke($null, @([double]25)) -eq 480) ''
Check 'חלון ב-60fps - הרצפה' ($sc.GetMethod('WindowMs', $ST).Invoke($null, @([double]60)) -eq 400) ''
Check 'קצב לא ידוע = 25' ($sc.GetMethod('WindowMs', $ST).Invoke($null, @([double]0)) -eq 480) ''

$l = Cues @(,@(10300, 13000)); [void](RunSnap $l @(10000) 30)
Check 'התחלה 300ms אחרי חיתוך - נצמדת' ((St $l 0) -eq 10000) (Times $l)
$l = Cues @(,@(9700, 13000)); [void](RunSnap $l @(10000) 30)
Check 'התחלה 300ms לפני חיתוך - נצמדת' ((St $l 0) -eq 10000) (Times $l)
$l = Cues @(,@(10600, 13000)); [void](RunSnap $l @(10000) 30)
Check 'התחלה 600ms אחרי - מחוץ לחלון' ((St $l 0) -eq 10600) (Times $l)
$l = Cues @(,@(8000, 12000)); [void](RunSnap $l @(10000) 30)
Check 'החיתוך באמצע הכתובית - לא זזה' ((Times $l) -eq '8000-12000') (Times $l)
$l = Cues @(,@(5000, 9800)); [void](RunSnap $l @(10000) 30)
Check 'סוף 200ms לפני חיתוך - מתארך לשני פריימים לפניו' ((En $l 0) -eq 9933) (Times $l)
$l = Cues @(,@(5000, 10200)); [void](RunSnap $l @(10000) 30)
Check 'סוף 200ms אחרי חיתוך - מתקצר' ((En $l 0) -eq 9933) (Times $l)

$l = Cues @(@(5000, 9900), @(10150, 13000)); $r = RunSnap $l @(10000) 30
Check 'הצמד הקלאסי: נגמרת, חיתוך, מתחילה' ((Times $l) -eq '5000-9933 10000-13000') (Times $l)
Check 'ספירת התוצאה' ($r.Cues -eq 2 -and $r.Starts -eq 1 -and $r.Ends -eq 1) ("cues=" + $r.Cues + " starts=" + $r.Starts + " ends=" + $r.Ends)

# הבאג הראשון: הסוף של A מתארך מעבר להתחלה המקורית של B (כי B עומדת
# לזוז לחיתוך). בדיקת חפיפה ״תוך כדי״ ראתה את זה כחפיפה - ו-B דילגה.
$l = Cues @(@(5000, 9700), @(9800, 13000)); [void](RunSnap $l @(10000) 30)
Check 'הארכה שסומכת על הבאה - והבאה באמת זזה' ((Times $l) -eq '5000-9933 10000-13000') (Times $l)

# הבאג השני: B חופפת ל-C ולכן לא זזה. אסור ש-A תתארך לתוכה.
$l = Cues @(@(5000, 9700), @(9750, 12000), @(11000, 14000)); [void](RunSnap $l @(10000) 30)
Check 'לא מאריכים לתוך כתובית שלא תזוז' ((En $l 0) -le (St $l 1)) (Times $l)
Check 'וחפיפה קיימת נשארת כמו שהיא' ((St $l 1) -eq 9750 -and (St $l 2) -eq 11000) (Times $l)

$l = Cues @(@(5000, 10300), @(10100, 13000)); [void](RunSnap $l @(10000) 30)
Check 'שתי כתוביות חופפות - אף אחת לא זזה' ((Times $l) -eq '5000-10300 10100-13000') (Times $l)

$l = Cues @(,@(10350, 10650)); [void](RunSnap $l @(10000) 30)
Check 'לא מקצרים מתחת ל-700ms' ((Times $l) -eq '10350-10650') (Times $l)
$l = Cues @(,@(9000, 9500)); [void](RunSnap $l @(9400) 30)
Check 'גם בסוף' ((Times $l) -eq '9000-9500') (Times $l)
$l = Cues @(,@(10300, 13000, 'u')); [void](RunSnap $l @(10000) 30)
Check 'שורה בלי תזמון אמיתי - לא זזה' ((Times $l) -eq '10300-13000') (Times $l)

$l = Cues @(,@(5000, 9550)); [void](RunSnap $l @(10000) 30)
Check '450ms ב-30fps - מחוץ לחלון' ((En $l 0) -eq 9550) (Times $l)
$l = Cues @(,@(5000, 9550)); [void](RunSnap $l @(10000) 25)
Check 'אותו דבר ב-25fps - בתוך החלון, ריווח 80ms' ((En $l 0) -eq 9920) (Times $l)
$l = Cues @(,@(5000, 9650)); [void](RunSnap $l @(10000) 60)
Check '60fps - ריווח של שני פריימים = 33ms' ((En $l 0) -eq 9967) (Times $l)

$l = Cues @(,@(10200, 13000)); [void](RunSnap $l @(9900, 10100) 30)
Check 'שני חיתוכים בחלון - הקרוב מנצח' ((St $l 0) -eq 10100) (Times $l)
$l = Cues @(,@(10000, 13000)); $r = RunSnap $l @(10000) 30
Check 'כבר על החיתוך - לא נספר כשינוי' ($r.Cues -eq 0) ("cues=" + $r.Cues)

# ================= 3. מבחן אקראי =================
# הבטחות שאסור שיישברו על שום קלט: אין חפיפה חדשה, הסדר נשמר, קצה זז
# רק לחיתוך ורק בתוך החלון, ושום כתובית שזזה לא קצרה מ-700ms.
Write-Host 'מבחן אקראי'
$rnd = New-Object Random 20260913
$trials = 400; $bad = @(); $moved = 0
for ($t = 0; $t -lt $trials; $t++) {
    $fps = @(24, 25, 30, 50, 60)[$rnd.Next(5)]
    $win = $sc.GetMethod('WindowMs', $ST).Invoke($null, @([double]$fps))
    $gap = $sc.GetMethod('GapMs', $ST).Invoke($null, @([double]$fps))
    $pairs = @(); $pos = $rnd.Next(0, 2000)
    for ($k = 0; $k -lt 14; $k++) {
        $dur = $rnd.Next(500, 4000)
        $pairs += ,@($pos, ($pos + $dur))
        $pos += $dur + @(0, 0, 40, 120, 300, 900)[$rnd.Next(6)]
    }
    $cuts = @(); $c = $rnd.Next(0, 1500)
    while ($c -lt $pos + 1000) { $cuts += $c; $c += $rnd.Next(150, 3500) }
    $l = Cues $pairs
    $orig = @(); foreach ($q in $l) { $orig += ,@([long]$fS.GetValue($q), [long]$fE.GetValue($q)) }
    [void](RunSnap $l $cuts $fps)
    for ($k = 0; $k -lt $l.Count; $k++) {
        $s = St $l $k; $e = En $l $k; $os = $orig[$k][0]; $oe = $orig[$k][1]
        if ($k -gt 0 -and $s -lt (En $l ($k-1))) { $bad += "t$t k$k overlap"; }
        if ($k -gt 0 -and $s -lt (St $l ($k-1))) { $bad += "t$t k$k order"; }
        if ($s -ne $os) {
            $moved++
            if (-not ($cuts -contains $s) -or [Math]::Abs($s - $os) -gt $win) { $bad += "t$t k$k start $os->$s" }
        }
        if ($e -ne $oe) {
            $moved++
            if (-not ($cuts -contains ($e + $gap)) -or [Math]::Abs($e + $gap - $oe) -gt $win) { $bad += "t$t k$k end $oe->$e" }
        }
        if (($s -ne $os -or $e -ne $oe) -and ($e - $s) -lt 700) { $bad += "t$t k$k short $($e-$s)" }
    }
}
Check "$trials סידורים - אין חפיפה, סדר, או קפיצה מחוץ לחלון" ($bad.Count -eq 0) (($bad | Select-Object -First 5) -join '; ')
Check 'והמבחן באמת הזיז משהו' ($moved -gt 500) "moved=$moved"

# ================= 4. הצמדה בגרירה על הציר =================
Write-Host 'גרירה על הציר'
$tlT = T 'TimelineControl'
$tl = [Activator]::CreateInstance($tlT)
$tlT.GetField('DurationMs').SetValue($tl, [long]60000)
$tlT.GetField('PxPerSec').SetValue($tl, [double]100)     # פיקסל = 10ms
$tlT.GetField('Position').SetValue($tl, [long]50000)
$tlT.GetField('Cuts').SetValue($tl, (Longs @(10000, 20000)))
$snapTl = $tlT.GetMethod('Snap', $IN)
$px = (T 'Theme').GetMethod('S', $ST, $null, [Type[]]@([double]), $null).Invoke($null, @([double]7))
$near = 10000 + ($px - 2) * 10; $far = 10000 + ($px + 3) * 10
Check 'קצה ליד חיתוך נצמד אליו' ($snapTl.Invoke($tl, (Pack ([long]$near) $null)) -eq 10000) "px=$px at=$near"
Check 'ורחוק ממנו - לא' ($snapTl.Invoke($tl, (Pack ([long]$far) $null)) -eq $far) "at=$far"
$tlT.GetField('Cuts').SetValue($tl, $null)
Check 'בלי חיתוכים - בלי הצמדה' ($snapTl.Invoke($tl, (Pack ([long]$near) $null)) -eq $near) ''
$tl.Dispose()

# ================= 5. איתור אמיתי =================
Write-Host 'איתור בסרט עם חיתוכים ידועים'
(T 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$ffT = T 'Ff'
$ff = $ffT.GetProperty('Exe', $ST).GetValue($null, $null)
if (-not $ff) { Write-Host 'no ffmpeg'; exit 1 }
$work = Join-Path $env:TEMP 'ss-scenecuts'
if (-not (Test-Path $work)) { New-Item -ItemType Directory $work | Out-Null }

function FfRun($ffArgs) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $ff -hide_banner -loglevel error -y @ffArgs 2>&1 | Out-Null
    $ErrorActionPreference = $old
}

# מחזיר את החיתוכים שהתוכנה מוצאת - דרך אותה עבודה ואותו איסוף שורות
function Detect($path, $durMs, $fps) {
    $sink = [Activator]::CreateInstance($listLongT)
    $job = $sc.GetMethod('MakeJob', $ST).Invoke($null, (Pack ([string]$path) ([long]$durMs) $sink))
    [void]$ffT.GetMethod('RunJob', $ST).Invoke($null, (Pack $job))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $job.Done -and $sw.Elapsed.TotalSeconds -lt 180) { Start-Sleep -Milliseconds 100 }
    if (-not $job.Succeeded) { Write-Host ('   job failed: ' + (($job.Log.ToString() -split "`n" | Select-Object -Last 4) -join ' | ')) }
    return ,@($norm.Invoke($null, (Pack $sink ([double]$fps))))
}

function Compare-Cuts($name, $found, $truth, $tolMs) {
    $miss = @(); foreach ($x in $truth) { if (-not ($found | Where-Object { [Math]::Abs($_ - $x) -le $tolMs })) { $miss += $x } }
    $extra = @(); foreach ($x in $found) { if (-not ($truth | Where-Object { [Math]::Abs($_ - $x) -le $tolMs })) { $extra += $x } }
    $worst = 0
    foreach ($x in $truth) { foreach ($y in $found) { $d = [Math]::Abs($x - $y); if ($d -le $tolMs -and $d -gt $worst) { $worst = $d } } }
    Check "$name - כל החיתוכים נמצאו" ($miss.Count -eq 0) ("חסרים: " + ($miss -join ','))
    Check "$name - בלי חיתוכים מדומים" ($extra.Count -eq 0) ("מיותרים: " + ($extra -join ','))
    Check "$name - סטייה עד פריים אחד" ($worst -le $tolMs) ("סטייה מרבית ${worst}ms; נמצא: " + ($found -join ','))
}

# סינתטי: ארבעה ״שוטים״ שונים לגמרי, ובתוכם תנועה (מקורות מונפשים וזום
# של מנדלברוט) - כדי שחיתוך מדומה מתנועה ייתפס.
$synth = Join-Path $work 'synth-cuts.mp4'
FfRun @('-f','lavfi','-i','testsrc2=s=320x180:r=25:d=3',
        '-f','lavfi','-i','smptehdbars=s=320x180:r=25:d=2',
        '-f','lavfi','-i','mandelbrot=s=320x180:r=25,trim=duration=2.4',
        '-f','lavfi','-i','rgbtestsrc=s=320x180:r=25:d=2',
        '-f','lavfi','-i','testsrc2=s=320x180:r=25:d=2,hue=h=120',
        '-filter_complex','[0:v]format=yuv420p,setsar=1[a];[1:v]format=yuv420p,setsar=1[b];[2:v]format=yuv420p,setsar=1[c];[3:v]format=yuv420p,setsar=1[d];[4:v]format=yuv420p,setsar=1[e];[a][b][c][d][e]concat=n=5:v=1:a=0[v]',
        '-map','[v]','-c:v','libx264','-preset','veryfast','-crf','20', $synth)
if (Test-Path $synth) {
    $found = Detect $synth 11400 25
    Compare-Cuts 'סינתטי' $found @(3000, 5000, 7400, 9400) 40
} else { Check 'נוצר סרט הבדיקה הסינתטי' $false $synth }

# צילומים אמיתיים (אם יש): ארבעה שוטים מ-focus-video, חיתוכים חדים ביניהם
$shots = @('s01','s02','s03','s04') | ForEach-Object { "D:\Claude\focus-video\shots\$_.mp4" }
if (($shots | Where-Object { Test-Path $_ }).Count -eq 4) {
    $real = Join-Path $work 'real-cuts.mp4'
    $fa = @(); foreach ($s in $shots) { $fa += @('-i', $s) }
    $fc = ''; for ($i = 0; $i -lt 4; $i++) { $fc += "[${i}:v:0]fps=30,scale=640:360,setsar=1,format=yuv420p[v$i];" }
    $fa += @('-filter_complex', ($fc + '[v0][v1][v2][v3]concat=n=4:v=1:a=0[v]'), '-map', '[v]', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '22', $real)
    FfRun $fa
    # מספרי הפריימים נספרים מהשוטים עצמם - לא מניחים אורך
    $truth = @(); $acc = 0
    foreach ($s in $shots[0..2]) {
        $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $o = & $ff -hide_banner -nostdin -i $s -map 0:v:0 -f null - 2>&1 | Out-String
        $ErrorActionPreference = $old
        $fr = [int]([regex]::Matches($o, 'frame=\s*(\d+)') | Select-Object -Last 1).Groups[1].Value
        $acc += $fr; $truth += [long][Math]::Round($acc * 1000.0 / 30)
    }
    $found = Detect $real 21000 30
    Compare-Cuts 'צילומים אמיתיים' $found $truth 34
} else {
    Write-Host '  SKIP  צילומים אמיתיים - D:\Claude\focus-video\shots לא נמצא (3 בדיקות לא רצו)' -ForegroundColor Yellow
}

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
