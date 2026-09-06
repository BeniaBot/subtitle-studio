# בדיקת עומס: כמה כתוביות התוכנה באמת מחזיקה, ואיפה היא מתחילה להיחנק.
#
# שיעור של שלוש שעות עם אלפי כתוביות הוא קלט לגמרי ריאלי לקהל הזה,
# ומחסנית הביטול שומרת תמונת מצב של **כל** המסמך בכל פעולה - זה המקום
# שהכי סביר שייפול.
#
#   powershell -ExecutionPolicy Bypass -File build\test-stress.ps1
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }; return ,$a }

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}   {1}" -f $name, $detail) }
    else { $script:fail++; Write-Host ("  FAIL {0}   {1}" -f $name, $detail) -ForegroundColor Red }
}

$docT = T 'Doc'; $cueT = T 'Cue'; $fmt = T 'Formats'
$cueCtor = $cueT.GetConstructor([Type[]]@([long],[long],[string]))

function BuildDoc($n) {
    $d = [Activator]::CreateInstance($docT)
    $list = $docT.GetField('Cues').GetValue($d)
    for ($i = 0; $i -lt $n; $i++) {
        $a = [long]($i * 3000)
        # חפיפה מכוונת בכל עשירית, כדי ש-FixOverlaps יהיה לו מה לעשות
        $b = [long]($a + $(if ($i % 10 -eq 0) { 3500 } else { 2500 }))
        $list.Add($cueCtor.Invoke(@($a, $b, [string]("שורה מספר " + $i + " עם קצת טקסט"))))
    }
    return ,$d
}

function Ms($block) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $block | Out-Null
    return $sw.ElapsedMilliseconds
}

Write-Host 'מסמכים גדולים'
foreach ($n in @(5000, 20000, 50000)) {
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    $mem0 = [GC]::GetTotalMemory($true)

    $tBuild = Ms { $script:doc = BuildDoc $n }
    $doc = $script:doc
    $cues = $docT.GetField('Cues').GetValue($doc)

    $tSort  = Ms { $docT.GetMethod('Sort').Invoke($doc, @()) }
    $tCount = Ms { $script:ov = $docT.GetMethod('CountOverlaps').Invoke($doc, @()) }
    $tFix   = Ms { $docT.GetMethod('FixOverlaps').Invoke($doc, (Pack ([int]80))) }
    $tShift = Ms { $docT.GetMethod('Shift').Invoke($doc, (Pack $cues ([long]500))) }

    # מחסנית הביטול: כל Push מעתיק את כל המסמך
    $tPush = Ms { for ($k = 0; $k -lt 10; $k++) { $docT.GetMethod('Push').Invoke($doc, (Pack ([string]'x'))) } }
    $tUndo = Ms { $docT.GetMethod('Undo').Invoke($doc, @()) }

    $tmp = Join-Path $env:TEMP ('ss-stress-' + $n + '.srt')
    $styleT = T 'SubStyle'; $style = [Activator]::CreateInstance($styleT)
    $sf = [Enum]::Parse((T 'SubFormat'), 'Srt')
    $tSave = Ms { $fmt.GetMethod('Save').Invoke($null, (Pack ([string]$tmp) $cues $sf $style ([int]1920) ([int]1080) $true)) }
    $tLoad = Ms { $script:back = $fmt.GetMethod('Load').Invoke($null, (Pack ([string]$tmp))) }
    $backCues = (T 'ParseResult').GetField('Cues').GetValue($script:back)

    $mem1 = [GC]::GetTotalMemory($false)
    $mb = [Math]::Round(($mem1 - $mem0) / 1MB, 1)

    Write-Host ("  --- $n כתוביות ---")
    Write-Host ("      בנייה $tBuild ms · מיון $tSort · ספירת חפיפות $tCount · תיקון $tFix · הזזה $tShift")
    Write-Host ("      Push x10 $tPush ms · Undo $tUndo ms · שמירה $tSave ms · טעינה $tLoad ms · זיכרון ~$mb MB")

    Check "טעינה חוזרת שמרה על הכול ב-$n" ($backCues.Count -eq $n) ("got=" + $backCues.Count)
    # סף שרירותי אבל מציאותי: מעל שנייה לפעולה זה כבר מרגיש תקוע
    Check "מיון סביר ב-$n"        ($tSort -lt 2000)  ("$tSort ms")
    Check "תיקון חפיפות סביר ב-$n" ($tFix -lt 4000)  ("$tFix ms")
    Check "ביטול סביר ב-$n"       ($tUndo -lt 1500)  ("$tUndo ms")
    Check "טעינה סבירה ב-$n"      ($tLoad -lt 8000)  ("$tLoad ms")
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    $script:doc = $null; $script:back = $null
}

# ---------- קלט עוין ----------
Write-Host ''
Write-Host 'קלט עוין'
$hostile = @(
    @('שורה אחת ענקית',     ("1`n00:00:01,000 --> 00:00:02,000`n" + ('א' * 200000))),
    @('סוף לפני התחלה',      "1`n00:00:09,000 --> 00:00:02,000`nהפוך`n"),
    @('זמן שלילי',           "1`n-00:00:05,000 --> 00:00:02,000`nשלילי`n"),
    @('מעל 99 שעות',         "1`n99:59:59,999 --> 99:59:59,999`nרחוק`n"),
    @('בייטי אפס באמצע',     "1`n00:00:01,000 --> 00:00:02,000`nלפני" + [char]0 + "אחרי`n"),
    @('שורות מעורבות',       "1`r00:00:01,000 --> 00:00:02,000`r`nטקסט`n`r"),
    @('תגיות ASS מקוננות',   "1`n00:00:01,000 --> 00:00:02,000`n{\pos(1,2)}{\an8}{\b1}טקסט{\b0}`n"),
    @('בלי שום זמנים',       "סתם טקסט`nעוד שורה`n"),
    @('ריק לגמרי',           ''),
    @('רק רווחים',           "   `n`n   `n")
)
foreach ($h in $hostile) {
    $ok = $true; $detail = ''
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $c = $fmt.GetMethod('ParseSrt').Invoke($null, (Pack ([string]$h[1])))
        $detail = "" + $c.Count + " cues, " + $sw.ElapsedMilliseconds + " ms"
        if ($sw.ElapsedMilliseconds -gt 5000) { $ok = $false; $detail += ' - איטי מדי' }
    } catch {
        $ok = $false
        $detail = 'חריגה: ' + $_.Exception.InnerException.GetType().Name
    }
    Check ('שורד: ' + $h[0]) $ok $detail
}

# קובץ שהוא בכלל לא כתוביות
$jpg = [byte[]](0xFF,0xD8,0xFF,0xE0) + (New-Object byte[] 5000)
$tmp2 = Join-Path $env:TEMP 'ss-stress-fake.srt'
[IO.File]::WriteAllBytes($tmp2, $jpg)
try {
    $r = $fmt.GetMethod('Load').Invoke($null, (Pack ([string]$tmp2)))
    $rc = (T 'ParseResult').GetField('Cues').GetValue($r)
    Check 'שורד: JPEG בשם srt' $true ("" + $rc.Count + " cues")
} catch {
    Check 'שורד: JPEG בשם srt' $false ('חריגה: ' + $_.Exception.InnerException.GetType().Name)
}
Remove-Item $tmp2 -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
