# בדיקות ממשק בלי עכבר ובלי גניבת מיקוד.
#
# למה לא לחיצות אמיתיות: SetForegroundWindow נחסם כשהתהליך שמריץ אותו אינו
# בחזית, ואז הקליק נופל על חלון אחר בלי שום שגיאה - הבדיקה "עוברת" בשקר.
# כאן בונים את החלון בתוך התהליך, מחוץ למסך, ומפעילים את אותם מטפלי אירועים
# שהלחיצה הייתה מפעילה.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\Subtext.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exe))
$formT = $asm.GetType('SubtitleStudio.MainForm')
$cueT  = $asm.GetType('SubtitleStudio.Cue')
$NP    = [Reflection.BindingFlags]'NonPublic,Instance'

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}" -f $name) }
    else { $script:fail++; Write-Host ("  FAIL {0}  {1}" -f $name, $detail) -ForegroundColor Red }
}
function Eq($name, $got, $want) { Check $name ($got -eq $want) ("got=$got want=$want") }
function Pack {
    $a = New-Object object[] $args.Count
    for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }
    return ,$a
}

function Fld($obj, $name) { return $formT.GetField($name, $NP).GetValue($obj) }
function Call($obj, $name, $argv) { return $formT.GetMethod($name, $NP).Invoke($obj, $argv) }

# הפעלת מטפל הלחיצה של פקד - בדיוק מה שלחיצה אמיתית הייתה עושה
function Tap($ctrl) {
    $m = $ctrl.GetType().GetMethod('OnClick', $NP)
    if (-not $m) { throw 'OnClick not found' }
    $m.Invoke($ctrl, (Pack ([EventArgs]::Empty))) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
}

function CloseExtraForms($keep) {
    for ($i = [System.Windows.Forms.Application]::OpenForms.Count - 1; $i -ge 0; $i--) {
        $w = [System.Windows.Forms.Application]::OpenForms[$i]
        if ($w -ne $keep) { $w.Close() }
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function NewForm($w, $h) {
    $f = [Activator]::CreateInstance($formT)
    $f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $f.Location = New-Object System.Drawing.Point -3000, -3000
    $f.ClientSize = New-Object System.Drawing.Size $w, $h
    $f.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $f.ClientSize = New-Object System.Drawing.Size $w, $h
    [System.Windows.Forms.Application]::DoEvents()
    return $f
}

function AddCue($doc, $a, $b, $text) {
    $list = $doc.GetType().GetField('Cues').GetValue($doc)
    $c = $cueT.GetConstructor([Type[]]@([long],[long],[string])).Invoke(@([long]$a,[long]$b,[string]$text))
    $list.Add($c)
    return $c
}

# ================= הפעולה הבסיסית: כתובית חדשה =================
Write-Host 'הכפתור הראשי'
$f = NewForm 1493 997
$doc = Fld $f '_doc'
$cues = $doc.GetType().GetField('Cues').GetValue($doc)
AddCue $doc 1000 3000 'ראשונה' | Out-Null
$doc.GetType().GetMethod('RaiseChanged').Invoke($doc, @()) | Out-Null
Call $f 'SyncAfterDocChange' (Pack) | Out-Null

$before = $cues.Count
Tap (Fld $f '_addCueBtn')
Eq 'כתובית חדשה נוספה' $cues.Count ($before + 1)

# הכתובית החדשה מתחילה במקום שבו הנגן עומד
$eng = Fld $f '_engine'
$pos = $eng.GetType().GetProperty('Position').GetValue($eng, $null)
$new = $null
foreach ($c in $cues) { if ($c.Text -eq '') { $new = $c } }
Check 'נוצרה במיקום הנגן' ($null -ne $new -and [Math]::Abs($new.Start - $pos) -lt 200) `
    ("start=" + $(if ($new) { $new.Start } else { 'null' }) + " pos=$pos")
Check 'הכתובית החדשה מסומנת' ($null -ne $new -and $new.Selected) ''

# חיפוש (Ctrl+F): ״2 מתוך 3״ - איזו התאמה זו ומתוך כמה (0.8.41)
AddCue $doc 4000 5000 'שָׁלוֹם לכולם' | Out-Null
AddCue $doc 6000 7000 'עולם' | Out-Null
AddCue $doc 8000 9000 "אמר שלום`nושלום" | Out-Null
$doc.GetType().GetMethod('RaiseChanged').Invoke($doc, @()) | Out-Null
Call $f 'SyncAfterDocChange' (Pack) | Out-Null
$fs1 = Call $f 'FindStep' (Pack 'שלום' $true); $fa = Pack 'שלום' 0; $n1 = $formT.GetMethod('FindPosition', $NP).Invoke($f, $fa); $t1 = $fa[1]
$fs2 = Call $f 'FindStep' (Pack 'שלום' $true); $fa = Pack 'שלום' 0; $n2 = $formT.GetMethod('FindPosition', $NP).Invoke($f, $fa); $t2 = $fa[1]
Check 'חיפוש: ״1 מתוך 2״ ואז ״2 מתוך 2״ (בלי ניקוד, כתובית עם שני מופעים נספרת פעם אחת)' ($fs1 -and $fs2 -and $n1 -eq 1 -and $n2 -eq 2 -and $t1 -eq 2 -and $t2 -eq 2) "found=$fs1/$fs2 nth=$n1/$n2 total=$t1/$t2"
# החיפוש נפתח עם המילה שמסומנת בעורך; בלי סימון - עם החיפוש הקודם (0.8.41)
$tbx = Fld $f '_text'; $tbxWas = $tbx.Text; $tbx.Text = 'אמר שלום ושלום'; $tbx.Select(4, 4); $seed1 = Call $f 'FindSeed' (Pack)
$tbx.Select(0, 0); $formT.GetField('_findTerm', $NP).SetValue($f, 'קודם'); $seed2 = Call $f 'FindSeed' (Pack); $tbx.Text = $tbxWas
Check 'חיפוש נפתח עם המילה המסומנת, ובלי סימון - עם הקודם' ($seed1 -eq 'שלום' -and $seed2 -eq 'קודם') "sel=$seed1 none=$seed2"
# ״נמצא מופע אחד״, ״בכתובית אחת״ - לא ״נמצאו 1 מופעים ב־1 כתוביות״ (0.8.41)
$cntM = $asm.GetType('SubtitleStudio.ReplaceDlg').GetMethod('Counted', [Reflection.BindingFlags]'NonPublic,Public,Static')
$c1 = $cntM.Invoke($null, (Pack 1 1 $false)); $c2 = $cntM.Invoke($null, (Pack 3 1 $false)); $c3 = $cntM.Invoke($null, (Pack 3 2 $true))
Check 'חיפוש והחלפה: יחיד בעברית תקינה' ($c1 -eq 'נמצא מופע אחד.' -and $c2 -eq 'נמצאו 3 מופעים בכתובית אחת.' -and $c3 -like 'הוחלפו 3 מופעים ב*2 כתוביות.*Ctrl+Z.') "1/1=$c1 | 3/1=$c2 | 3/2=$c3"

# ================= חלוניות קופצות =================
Write-Host 'חלוניות קופצות'
foreach ($pair in @(@('_volBtn', 'PopupPanel'), @('_speedBtn', 'PopupPanel'))) {
    CloseExtraForms $f
    $n = [System.Windows.Forms.Application]::OpenForms.Count
    Tap (Fld $f $pair[0])
    $opened = $null
    foreach ($w in [System.Windows.Forms.Application]::OpenForms) {
        if ($w -ne $f) { $opened = $w }
    }
    Check ($pair[0] + ' פותח ' + $pair[1]) ($null -ne $opened -and $opened.GetType().Name -eq $pair[1]) `
        ("opened=" + $(if ($opened) { $opened.GetType().Name } else { 'nothing' }))
}
CloseExtraForms $f

# הסליידר חוזר הביתה אחרי שהחלונית נסגרת, אחרת אי אפשר לפתוח שוב
$vol = Fld $f '_volume'
$card = Fld $f '_videoCard'
Check 'הסליידר חזר לכרטיס' ($vol.Parent -eq $card) ("parent=" + $(if ($vol.Parent) { $vol.Parent.GetType().Name } else { 'null' }))
# ‏0.8.9: גם חלונית המהירות (פס וכפתורים) חוזרת הביתה - אחרת החלונית שנסגרת משמידה אותה
$spk = Fld $f '_speedPicker'
Check 'חלונית המהירות חזרה לכרטיס' ($spk -ne $null -and $spk.Parent -eq $card) ("parent=" + $(if ($spk -and $spk.Parent) { $spk.Parent.GetType().Name } else { 'null' }))
Tap (Fld $f '_volBtn')
$again = $null
foreach ($w in [System.Windows.Forms.Application]::OpenForms) { if ($w -ne $f) { $again = $w } }
Check 'אפשר לפתוח את העוצמה שוב' ($null -ne $again) ''
CloseExtraForms $f

# ================= קיצורי מקלדת =================
Write-Host 'קיצורים'
$K = [System.Windows.Forms.Keys]
$eng.GetType().GetMethod('Seek').Invoke($eng, (Pack ([long]10000))) | Out-Null
Call $f 'HandleShortcut' (Pack ($K::Right -bor $K::Control)) | Out-Null
$p1 = $eng.GetType().GetProperty('Position').GetValue($eng, $null)
Eq 'Ctrl+ימינה קופץ שתי שניות' $p1 12000
Call $f 'HandleShortcut' (Pack ($K::Left -bor $K::Control -bor $K::Shift)) | Out-Null
$p2 = $eng.GetType().GetProperty('Position').GetValue($eng, $null)
Eq 'Ctrl+Shift+שמאלה קופץ חצי שנייה' $p2 11500

$sp0 = $eng.GetType().GetProperty('Speed').GetValue($eng, $null)
Call $f 'HandleShortcut' (Pack ($K::Down -bor $K::Control)) | Out-Null
$sp1 = $eng.GetType().GetProperty('Speed').GetValue($eng, $null)
Check 'Ctrl+למטה מאט' ($sp1 -lt $sp0) "from=$sp0 to=$sp1"
Call $f 'HandleShortcut' (Pack ($K::Up -bor $K::Control)) | Out-Null
$sp2 = $eng.GetType().GetProperty('Speed').GetValue($eng, $null)
Eq 'Ctrl+למעלה חוזר' $sp2 $sp0

# ================= כתובית ריקה לא נשמרת =================
Write-Host 'שמירה'
$tmp = Join-Path $env:TEMP ('ss_ui_' + [guid]::NewGuid().ToString('N').Substring(0,8) + '.srt')
$doc.GetType().GetField('FilePath').SetValue($doc, $tmp)
$n0 = $cues.Count
$ok = Call $f 'SaveSubtitles' (Pack $false)
Check 'השמירה הצליחה' $ok ''
Check 'הכתובית הריקה הושמטה' ($cues.Count -eq $n0 - 1) ("before=$n0 after=" + $cues.Count)
if (Test-Path $tmp) {
    $txt = [IO.File]::ReadAllText($tmp)
    Check 'הקובץ נכתב עם הכתובית האמיתית' ($txt -match 'ראשונה') ''
    Remove-Item $tmp -Force
} else { Check 'הקובץ נוצר' $false $tmp }

$f.Close(); $f.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ================= תזמון בלחיצה =================
Write-Host 'תזמון בלחיצה'
$g = NewForm 1493 997
$gdoc = Fld $g '_doc'
$gcues = $gdoc.GetType().GetField('Cues').GetValue($gdoc)

# טקסט חופשי עם שורות ריקות -> שלוש כתוביות בלי תזמון אמיתי
$fmt = $asm.GetType('SubtitleStudio.Formats')
$optT = $asm.GetType('SubtitleStudio.Formats+TextImportOptions')
$o = [Activator]::CreateInstance($optT)
$optT.GetField('SplitByBlankLine').SetValue($o, $true)
$optT.GetField('MarkUntimed').SetValue($o, $true)
$txt = "שלום לכולם`r`n`r`nזו בדיקה של התזמון`r`n`r`nוזו השורה השלישית"
$made = ($fmt.GetMethods([Reflection.BindingFlags]'NonPublic,Public,Static') | Where-Object { $_.Name -eq 'ImportPlainText' -and $_.GetParameters().Count -eq 2 } | Select-Object -First 1).Invoke($null, (Pack ([string]$txt) $o))
Eq 'הטקסט התחלק לשלוש' $made.Count 3
$allUntimed = $true
foreach ($c in $made) { if (-not $c.Untimed) { $allUntimed = $false } }
Check 'כולן מסומנות כלא-מתוזמנות' $allUntimed ''

foreach ($c in $made) { $gcues.Add($c) }
$gdoc.GetType().GetMethod('RaiseChanged').Invoke($gdoc, @()) | Out-Null

# מדמים סרט פתוח, כדי שהפס יופיע
$miT = $asm.GetType('SubtitleStudio.MediaInfo')
$mi = [Activator]::CreateInstance($miT)
$miT.GetField('DurationSec').SetValue($mi, [double]40)
$miT.GetField('Path').SetValue($mi, [string]'test.mp4')
$formT.GetField('_mi', $NP).SetValue($g, $mi)
Call $g 'UpdateTapUi' (Pack) | Out-Null
$bar = Fld $g '_tapBar'
Check 'הפס מופיע כשיש שורות בלי תזמון' $bar.Visible ''
Check 'הפס מציין כמה נשארו' ($bar.Text -like '*3*') $bar.Text

# נכנסים למצב ידנית (StartTapping מנגן, ואין כאן קובץ אמיתי)
$formT.GetField('_tapping', $NP).SetValue($g, $true)
$formT.GetField('_tapIndex', $NP).SetValue($g, [int]0)
Call $g 'UpdateTapUi' (Pack) | Out-Null
$add = Fld $g '_addCueBtn'
Check 'הכפתור הגדול מראה את השורה הבאה' ($add.Text -like '*שלום לכולם*') $add.Text

$geng = Fld $g '_engine'
function SeekTo($ms) { $geng.GetType().GetMethod('Seek').Invoke($geng, (Pack ([long]$ms))) | Out-Null }

SeekTo 2000
Call $g 'TapHere' (Pack) | Out-Null
Eq 'השורה הראשונה מתחילה בלחיצה' $gcues[0].Start 2000
Check 'וכבר לא מסומנת כהערכה' (-not $gcues[0].Untimed) ''
Eq 'עברנו לשורה הבאה' ($formT.GetField('_tapIndex', $NP).GetValue($g)) 1
Check 'הבאות נדחפו אחריה' ($gcues[1].Start -ge $gcues[0].End) ("next=" + $gcues[1].Start + " prevEnd=" + $gcues[0].End)

SeekTo 6000
Call $g 'TapHere' (Pack) | Out-Null
Eq 'השנייה מתחילה בלחיצה' $gcues[1].Start 6000
# הראשונה לא נשארת תלויה עד 6 שניות: נחתכת לפי אורך הטקסט + חסד
Check 'הראשונה לא נשארת תלויה בשקט' ($gcues[0].End -lt 5500 -and $gcues[0].End -gt 2500) $gcues[0].End

SeekTo 9000
Call $g 'TapHere' (Pack) | Out-Null
Eq 'השלישית מתחילה בלחיצה' $gcues[2].Start 9000
Check 'המצב נסגר לבד בסוף' (-not $formT.GetField('_tapping', $NP).GetValue($g)) ''
$left = 0
foreach ($c in $gcues) { if ($c.Untimed) { $left++ } }
Eq 'לא נשארו שורות בלי תזמון' $left 0

# **כתובית חדשה בלי שתיים על המסך** (0.8.26): ״הוספה לפני״ בין שתי צמודות, ו״כתובית חדשה כאן״ באמצע כתובית - בלי חפיפה
$cueT2 = $asm.GetType('SubtitleStudio.Cue')
$gcues.Clear(); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]1000) ([long]4000) 'ראשונה'))); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]4080) ([long]7000) 'שנייה')))
Call $g 'SyncAfterDocChange' (Pack) | Out-Null
Call $g 'InsertCueBefore' (Pack ([int]1)) | Out-Null
$ins = @($gcues | Where-Object { $_.Text -eq '' })[0]
Check 'הוספה לפני בין צמודות: בלי חפיפה, והחדשה לפחות 0.7 שנייה' ($gdoc.CountOverlaps() -eq 0 -and $ins -ne $null -and ($ins.End - $ins.Start) -ge 700) ("overlaps=" + $gdoc.CountOverlaps() + " new=" + $ins.Start + "-" + $ins.End + " first=" + $gcues[0].Start + "-" + $gcues[0].End)
$gcues.Clear(); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]1000) ([long]4000) 'ראשונה'))); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]4080) ([long]7000) 'שנייה')))
Call $g 'SyncAfterDocChange' (Pack) | Out-Null
SeekTo 2500
Call $g 'NewCueAtPlayhead' (Pack) | Out-Null
$nw = @($gcues | Where-Object { $_.Text -eq '' })[0]
Check 'כתובית חדשה באמצע כתובית: כאן מתחילה, הקודמת נגמרת לפניה, בלי חפיפה' ($gdoc.CountOverlaps() -eq 0 -and $nw.Start -eq 2500 -and $gcues[0].End -le 2420) ("overlaps=" + $gdoc.CountOverlaps() + " new=" + $nw.Start + "-" + $nw.End + " first-end=" + $gcues[0].End)
# קודמת קצרה (1.2 שניות) - כמה שהיא יכולה, והשאר מתחילת הבאה; לכל אחת נשארות 0.7 (0.8.41; עד כאן - חפיפה)
$gcues.Clear(); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]1000) ([long]2200) 'קצרה'))); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]2280) ([long]6000) 'ארוכה')))
Call $g 'SyncAfterDocChange' (Pack) | Out-Null
Call $g 'InsertCueBefore' (Pack ([int]1)) | Out-Null
$ins = @($gcues | Where-Object { $_.Text -eq '' })[0]; $sh = @($gcues | Where-Object { $_.Text -eq 'קצרה' })[0]; $lg = @($gcues | Where-Object { $_.Text -eq 'ארוכה' })[0]
Check 'הוספה לפני אחרי כתובית קצרה: בלי חפיפה, החדשה 0.7, הקודמת והבאה לפחות 0.7' ($gdoc.CountOverlaps() -eq 0 -and ($ins.End - $ins.Start) -ge 700 -and ($sh.End - $sh.Start) -ge 700 -and ($lg.End - $lg.Start) -ge 700 -and $sh.End -lt $ins.Start -and $ins.End -lt $lg.Start) ("overlaps=" + $gdoc.CountOverlaps() + " short=" + $sh.Start + "-" + $sh.End + " new=" + $ins.Start + "-" + $ins.End + " long=" + $lg.Start + "-" + $lg.End)
$gdoc.Undo(); Call $g 'SyncAfterDocChange' (Pack) | Out-Null
$gcues = $gdoc.GetType().GetField('Cues').GetValue($gdoc)      # ביטול מחליף את הרשימה
Check 'וביטול אחד מחזיר את שתיהן' ($gcues.Count -eq 2 -and $gcues[0].End -eq 2200 -and $gcues[1].Start -eq 2280) ("n=" + $gcues.Count + " " + $gcues[0].End + " " + $gcues[1].Start)
# צמודות בלי רווח (1000-4000, 4000-7000): החדשה **לפני** השנייה ובלי חפיפה (עד 0.8.40 השנייה נחשבה ״הקודמת״ - חפיפה)
$gcues.Clear(); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]1000) ([long]4000) 'אחת'))); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]4000) ([long]7000) 'שתיים')))
Call $g 'SyncAfterDocChange' (Pack) | Out-Null
Call $g 'InsertCueBefore' (Pack ([int]1)) | Out-Null
$tc = $gdoc.GetType().GetField('Cues').GetValue($gdoc); $order = ($tc | ForEach-Object { if ($_.Text) { $_.Text } else { '*' } }) -join ','
$ins = @($tc | Where-Object { $_.Text -eq '' })[0]
Check 'הוספה לפני בין צמודות בלי רווח: החדשה בין שתיהן, בלי חפיפה' ($order -eq 'אחת,*,שתיים' -and $gdoc.CountOverlaps() -eq 0 -and ($ins.End - $ins.Start) -ge 700) ("order=$order overlaps=" + $gdoc.CountOverlaps() + " new=" + $ins.Start + "-" + $ins.End)
$gdoc.Undo(); Call $g 'SyncAfterDocChange' (Pack) | Out-Null; $gcues = $gdoc.GetType().GetField('Cues').GetValue($gdoc)
# **לחיצה על הציר היא לא עריכה** (0.8.26): בחירת כתובית בלחיצה לא נרשמת לביטול ולא מסמנת ״לא שמור״; לחיצה על מקום ריק בפס לא יוצרת
# כתובית; גרירה כן - וצעד ביטול אחד
$tlc = Fld $g '_tl'; $tlT = $tlc.GetType(); $NPP = [Reflection.BindingFlags]'NonPublic,Public,Instance'
$gcues.Clear(); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]2000) ([long]5000) 'שלום'))); $gcues.Add([Activator]::CreateInstance($cueT2, (Pack ([long]8000) ([long]11000) 'עולם')))
Call $g 'SyncAfterDocChange' (Pack) | Out-Null
$gdoc = Fld $g '_doc'
function TlMouse($name, $x, $y) { $ea = New-Object object[] 1; $ea[0] = (New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left), 1, $x, $y, 0).psobject.BaseObject; [void]$tlT.GetMethod($name, $NPP).Invoke($tlc, $ea) }
$msX = $tlT.GetMethod('MsToX', $NPP); $ty = [int]($tlT.GetProperty('TrackTop', $NPP).GetValue($tlc, $null) + 30)
$xc = [int]$msX.Invoke($tlc, (Pack ([long]3500))); $xe = [int]$msX.Invoke($tlc, (Pack ([long]6500)))
$gdoc.Dirty = $false; $gdoc.ClearHistory()
TlMouse 'OnMouseDown' $xc $ty; TlMouse 'OnMouseUp' $xc $ty
TlMouse 'OnMouseDown' $xe $ty; TlMouse 'OnMouseUp' $xe $ty
$cnt1 = $gdoc.GetType().GetField('Cues').GetValue($gdoc).Count
Check 'ציר: לחיצה לבחירה - בלי ״לא שמור״ ובלי צעד ביטול; לחיצה על מקום ריק - בלי כתובית חדשה' ((-not $gdoc.Dirty) -and (-not $gdoc.CanUndo) -and $cnt1 -eq 2) ("dirty=" + $gdoc.Dirty + " undo=" + $gdoc.CanUndo + " cues=" + $cnt1)
$s0 = $gdoc.GetType().GetField('Cues').GetValue($gdoc)[0].Start
TlMouse 'OnMouseDown' $xc $ty; TlMouse 'OnMouseMove' ($xc + 60) $ty; TlMouse 'OnMouseUp' ($xc + 60) $ty
$moved = $gdoc.GetType().GetField('Cues').GetValue($gdoc)[0].Start -ne $s0
$gdoc.Undo(); $back = $gdoc.GetType().GetField('Cues').GetValue($gdoc)[0].Start -eq $s0
Check 'ציר: גרירה מזיזה, וביטול אחד מחזיר' ($moved -and $back) ("moved=$moved back=$back")
$g.Close(); $g.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ================= סרט בלי פס קול =================
# הבאג: Waveform.Ready נשאר false, הציר צייר "מכין את פס הקול" לנצח
# ורענן 30 פעמים בשנייה. הקובץ נוצר על ידי build\make-testmedia.ps1.
$silent = Join-Path $env:TEMP 'ss-gallery\silent.mp4'
if (Test-Path $silent) {
    Write-Host 'סרט בלי קול'
    $q = NewForm 1400 900
    Call $q 'OpenMedia' (Pack ([string]$silent)) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
    $mi2 = Fld $q '_mi'
    Check 'הקובץ נפתח' ($null -ne $mi2) ''
    if ($null -ne $mi2) {
        Check 'זוהה כחסר אודיו' (-not $mi2.HasAudio) ("HasAudio=" + $mi2.HasAudio)
        $wave = Fld $q '_wave'
        Check 'פס הקול מסומן כמוכן' ($wave.Ready) 'אחרת הציר מצייר בלי הפסקה'
    }
    $q.Close(); $q.Dispose()
    [System.Windows.Forms.Application]::DoEvents()
}

# ================= תוויות הסרגל =================
# הבאג שהיה: במסך צר כל התוויות ירדו בבת אחת ונשארו אייקונים בלי הסבר.
Write-Host 'תוויות הסרגל'
foreach ($w in @(1920, 1493, 1175)) {
    $g = NewForm $w 850
    $btns = Fld $g '_toolbarBtns'
    $named = 0
    foreach ($b in $btns) { if (-not $b.IconOnly) { $named++ } }
    # אינדקס 2 = "כתוביות", 3 = "הסרט" - שני התפריטים שאי אפשר לנחש לפי אייקון
    Check ("תפריטי הסרגל מכותרים ב-$w") ((-not $btns[2].IconOnly) -and (-not $btns[3].IconOnly)) `
        ("named=$named subs=" + $btns[2].IconOnly + " video=" + $btns[3].IconOnly)
    $g.Close(); $g.Dispose()
    [System.Windows.Forms.Application]::DoEvents()
}

Write-Host 'תפריט הכתוביות'
# הפריטים שדורשים סרט חייבים להיות כבויים בלי סרט, אחרת המשתמש לוחץ
# ומקבל הודעת שגיאה במקום כפתור מעומעם.
$g = NewForm 1493 900
$items = $formT.GetMethod('SubtitleMenuItems', $NP).Invoke($g, @())
function ItemNamed($list, $needle) {
    foreach ($it in $list) { if ($it.Text -and $it.Text.Contains($needle)) { return $it } }
    return $null
}
$tr = ItemNamed $items 'תמלול'
Check 'יש פריט תמלול בתפריט' ($null -ne $tr) ''
if ($tr) {
    Check 'התמלול כבוי בלי סרט' (-not $tr.Enabled) ("enabled=" + $tr.Enabled)
    Check 'התיאור מזכיר שהקול נשלח' ($tr.Desc -and $tr.Desc.Contains('נשלח')) ("desc=" + $tr.Desc)
}
$g.Close(); $g.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# ועם סרט - הפריט נדלק
$g2 = NewForm 1493 900
$formT.GetField('_mi', $NP).SetValue($g2, $mi)
$items2 = $formT.GetMethod('SubtitleMenuItems', $NP).Invoke($g2, @())
$tr2 = ItemNamed $items2 'תמלול'
Check 'התמלול נדלק כשיש סרט' ($tr2 -and $tr2.Enabled) ''

# ---- גובה התפריט מול המסך ----
# 19 פעולות ב-125% צריכות כ-1,390 פיקסלים, ובמסך 1920x1200 יש 1,140.
# הפריטים התחתונים פשוט לא הוצגו - מ-0.6.4, ואף בדיקה לא ראתה.
$pmT = $asm.GetType('SubtitleStudio.PopupMenu')
$pm = $pmT.GetConstructors()[0].Invoke(@($items2, [int]350))
$arrange = $pmT.GetMethod('Arrange', $NP)
$rectsF = $pmT.GetField('_rects', $NP)
$colsP = $pmT.GetProperty('Columns', $NP)
$arrange.Invoke($pm, @([int]::MaxValue)) | Out-Null
$natural = $pm.ClientSize.Height
Eq 'עם מקום - עמודה אחת' $colsP.GetValue($pm, $null) 1
$limit = [int]($natural * 0.75)
$arrange.Invoke($pm, @([int]$limit)) | Out-Null
Check 'בלי מקום - נכנס לגובה' ($pm.ClientSize.Height -le $limit) ("h=" + $pm.ClientSize.Height + " limit=$limit natural=$natural")
Check 'ובעמודות' ($colsP.GetValue($pm, $null) -ge 2) ("cols=" + $colsP.GetValue($pm, $null))
$rects = $rectsF.GetValue($pm)
$inside = $true; $overlap = $false; $split = @()
$client = New-Object System.Drawing.Rectangle 0, 0, $pm.ClientSize.Width, $pm.ClientSize.Height
for ($i = 0; $i -lt $rects.Length; $i++) {
    if (-not $client.Contains($rects[$i])) { $inside = $false }
    for ($j = $i + 1; $j -lt $rects.Length; $j++) { if ($rects[$i].IntersectsWith($rects[$j])) { $overlap = $true } }
}
# קבוצה לא נחצית: כל פריט באותה עמודה כמו הכותרת שלו
$headX = -1
for ($i = 0; $i -lt $items2.Count; $i++) {
    if ($items2[$i].Header) { $headX = $rects[$i].X; continue }
    if ($headX -ge 0 -and $rects[$i].X -ne $headX) { $split += $items2[$i].Text }
}
Check 'כל הפריטים בתוך התפריט' $inside ''
Check 'אין פריטים דרוסים' (-not $overlap) ''
Check 'קבוצה לא נחצית בין עמודות' ($split.Count -eq 0) ($split -join ', ')
$pm.Dispose()

$cutsIt = ItemNamed $items2 'מעברי סצנה'
Check 'יש פריט הצמדה למעברי סצנה' ($null -ne $cutsIt) ''
$g2.Close(); $g2.Dispose()
[System.Windows.Forms.Application]::DoEvents()

# **שדה בכיוון של מה שכתוב בו** (0.8.22): ״Harry Potter, Rupert Grint.״ ברקע של התמלול הוצג ״.Harry Potter...״ בממשק העברי
$ST2 = [Reflection.BindingFlags]'NonPublic,Public,Static'
[void]$asm.GetType('SubtitleStudio.Lang').GetMethod('Set', $ST2).Invoke($null, @([string]'he'))
$hostF = New-Object Windows.Forms.Form; $hostF.RightToLeft = 'Yes'; $hostF.StartPosition = 'Manual'; $hostF.Location = New-Object Drawing.Point -6000, 0
$fld = [Activator]::CreateInstance($asm.GetType('SubtitleStudio.Field')); $hostF.Controls.Add($fld); [void]$hostF.Handle; [void]$fld.Box.Handle
$fld.Box.Text = 'Harry Potter, Rupert Grint.'; $d1 = [string]$fld.Box.RightToLeft
$fld.Box.Text = 'Harry Potter הוא הסרט'; $d2 = [string]$fld.Box.RightToLeft
$fld.Box.Text = '2024'; $d3 = [string]$fld.Box.RightToLeft
$fld.Box.Text = ''; $d4 = [string]$fld.Box.RightToLeft
$fld.Box.Text = 'Groq'; $fld.Box.Select(2, 0); $fld.Box.Text = 'Groq'; $car = $fld.Box.SelectionStart
$hostF.Dispose()
Check 'שדה: לועזית - משמאל לימין, עם עברית - מימין, מספר - בלי שינוי, ריק - כיוון הממשק' ($d1 -eq 'No' -and $d2 -eq 'Yes' -and $d3 -eq 'Yes' -and $d4 -eq 'Yes') "latin=$d1 mixed=$d2 number=$d3 empty=$d4"

# פס ההתקדמות של התרגום מתמלא מהצד שבו קוראים: מימין בעברית, משמאל באנגלית (עד 1.11 תמיד מימין)
function BarSide([string]$lang) {
    [void]$asm.GetType('SubtitleStudio.Lang').GetMethod('Set', $ST2).Invoke($null, @($lang))
    $pb = [Activator]::CreateInstance($asm.GetType('SubtitleStudio.ProgressBarLite')); $pb.Size = New-Object Drawing.Size 200, 10; $pb.Value = 0.3
    $bm = New-Object Drawing.Bitmap 200, 10; $pb.DrawToBitmap($bm, (New-Object Drawing.Rectangle 0, 0, 200, 10))
    $l = $bm.GetPixel(20, 5); $r = $bm.GetPixel(180, 5); $bm.Dispose(); $pb.Dispose()
    $acc = $asm.GetType('SubtitleStudio.Theme').GetProperty('Accent', $ST2).GetValue($null, $null)
    $near = { param($c) [Math]::Abs($c.R - $acc.R) + [Math]::Abs($c.G - $acc.G) + [Math]::Abs($c.B - $acc.B) -lt 30 }
    if ((& $near $l) -and -not (& $near $r)) { return 'left' } elseif ((& $near $r) -and -not (& $near $l)) { return 'right' } else { return '?' }
}
$mixM = $asm.GetType('SubtitleStudio.Theme').GetMethod('MixSuspect', $ST2)
$wrapped = 'מחפש פנים ' + [char]0x202A + '45' + [char]0x202C + '%'
Check 'הגלאי של טקסט הפוך: מספר עטוף ואחריו ״%״ - חשוד, ״45%״ רגיל - לא' ($mixM.Invoke($null, @([string]$wrapped)) -and -not $mixM.Invoke($null, @([string]'מחפש פנים 45%'))) ''
$sideEn = BarSide 'en'; $sideHe = BarSide 'he'
Check 'פס התקדמות התרגום: מימין בעברית, משמאל באנגלית' ($sideHe -eq 'right' -and $sideEn -eq 'left') "he=$sideHe en=$sideEn"

# **״למחוק את הכתוביות הקיימות״ בחלון התמלול** נקרא אחרי שהחלון נסגר - וחלון סגור מוסתר, כל פקד בו ״לא גלוי״. מ-0.6.0 עד 0.8.40
# המתג (שדולק מראש) לא עבד אף פעם: מי שתמלל שוב קיבל את החדשות על הישנות
$trT = $asm.GetType('SubtitleStudio.TranscribeDlg')
function TrClosed([bool]$on) {
    $a = New-Object object[] 5; $a[0] = $null; $a[1] = [int]3; $a[2] = [string]''; $a[3] = [long]-1; $a[4] = [long]-1
    $d = [Activator]::CreateInstance($trT, $a)
    $trT.GetField('_replace', $NP).GetValue($d).Checked = $on
    $d.StartPosition = 'Manual'; $d.Location = New-Object Drawing.Point -6000, -6000
    $tmr = New-Object Windows.Forms.Timer; $tmr.Interval = 300
    $tmr.Add_Tick({ $tmr.Stop(); $d.Close() }.GetNewClosure()); $tmr.Start()
    [void]$d.ShowDialog(); $r = $d.ReplaceExisting; $d.Dispose(); $tmr.Dispose()
    return $r
}
$rOn = TrClosed $true; $rOff = TrClosed $false
# לפני יצירת הסרט: שורות בלי תזמון - שואלים (0.8.42; עד כאן נצרבו בזמנים משוערים בלי מילה)
$bf = NewForm 1400 900
$bdoc = Fld $bf '_doc'; $bcues = $bdoc.GetType().GetField('Cues').GetValue($bdoc)
$u1 = AddCue $bdoc 1000 3000 'מתוזמנת'; $u2 = AddCue $bdoc 3000 5000 'בלי תזמון'; $u2.Untimed = $true
$script:askTitle = ''
$askT = New-Object Windows.Forms.Timer; $askT.Interval = 300
$askT.Add_Tick({ foreach ($w in @([Windows.Forms.Application]::OpenForms)) { if ($w -ne $bf -and $w.Text) { $script:askTitle = $w.Text; $w.Close() } } })
# במצב בדיקה (SUBSTUDIO_TEST) השאלות שלפני הצריבה מדולגות - כאן מכבים אותו לרגע, והטיימר סוגר את השאלה
$env:SUBSTUDIO_TEST = $null; $askT.Start(); $go = Call $bf 'OfferTidyBeforeBurn' (Pack); $askT.Stop(); $askT.Dispose(); $env:SUBSTUDIO_TEST = '1'
Check 'לפני יצירת הסרט: שורה בלי תזמון - שואלים, וסגירת השאלה עוצרת' ($script:askTitle -like '*לתזמן*' -and -not $go) "title=[$($script:askTitle)] go=$go"
$u2.Untimed = $false; $script:askTitle = ''
$askT = New-Object Windows.Forms.Timer; $askT.Interval = 300
$askT.Add_Tick({ foreach ($w in @([Windows.Forms.Application]::OpenForms)) { if ($w -ne $bf -and $w.Text) { $script:askTitle = $w.Text; $w.Close() } } })
$env:SUBSTUDIO_TEST = $null; $askT.Start(); $go2 = Call $bf 'OfferTidyBeforeBurn' (Pack); $askT.Stop(); $askT.Dispose(); $env:SUBSTUDIO_TEST = '1'
Check 'וכשהכול מתוזמן - בלי השאלה הזאת' ($script:askTitle -notlike '*לתזמן*') "title=[$($script:askTitle)] go=$go2"
$bf.Close(); $bf.Dispose()
# **הכפתור הירוק כשיש רק טשטוש** (0.8.44): בלי כתוביות ועם אזור - ״יצירת סרט עם טשטוש״, והחלון שנפתח לא מדבר על כתוביות (עד כאן
# ״יצירת סרט עם כתוביות״ ← ״הטמעת הכתוביות בסרט · 0 כתוביות״ עם בחירה בין צריבה לערוץ כתוביות)
$gf = NewForm 1493 997
$gsty = Fld $gf '_style'; $gsty.Zones.Add([Activator]::CreateInstance($asm.GetType('SubtitleStudio.BlurZone')))
$labBlur = [string]$formT.GetMethod('ExportLabel', $NP).Invoke($gf, @())
$gdoc2 = Fld $gf '_doc'; AddCue $gdoc2 1000 3000 'כתובית' | Out-Null
$labSubs = [string]$formT.GetMethod('ExportLabel', $NP).Invoke($gf, @())
Check 'הכפתור הירוק: רק טשטוש - ״יצירת סרט עם טשטוש״; עם כתובית - ״עם כתוביות״' ($labBlur -eq 'יצירת סרט עם טשטוש' -and $labSubs -eq 'יצירת סרט עם כתוביות') "blur=[$labBlur] subs=[$labSubs]"
$exT = $asm.GetType('SubtitleStudio.ExportVideoDlg'); $docT2 = $asm.GetType('SubtitleStudio.Doc')
$emptyDoc = [Activator]::CreateInstance($docT2)
$tmpV = Join-Path $env:TEMP 'ss-gallery\test.mp4'; if (-not (Test-Path $tmpV)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }
$vmi = $asm.GetType('SubtitleStudio.Ff').GetMethod('ProbeFile', [Reflection.BindingFlags]'NonPublic,Public,Static').Invoke($null, @([string]$tmpV))
$exArgs = New-Object object[] 6; $exArgs[0] = $null; $exArgs[1] = $emptyDoc; $exArgs[2] = $vmi; $exArgs[3] = $gsty; $exArgs[4] = [long]-1; $exArgs[5] = [long]-1
$exDlg = [Activator]::CreateInstance($exT, $exArgs)
$exTitle = [string]$exT.GetProperty('TitleText', $NP).GetValue($exDlg, $null); $exSub = [string]$exT.GetProperty('SubtitleText', $NP).GetValue($exDlg, $null)
$modeShown = $exT.GetField('_modeSoft', $NP).GetValue($exDlg).Parent -ne $null
$exJob = $exT.GetMethod('BuildJob', $NP).Invoke($exDlg, @([string](Join-Path $env:TEMP 'ss-blur-only.mp4'))); $exDlg.Dispose()
Check 'חלון היצירה בלי כתוביות: ״יצירת סרט עם טשטוש״, בלי ״0 כתוביות״, בלי הבחירה בערוץ כתוביות' ($exTitle -eq 'יצירת סרט עם טשטוש' -and -not $exSub.Contains('כתוביות') -and -not $modeShown -and ([string]$exJob.Title).Contains('טשטוש')) "title=[$exTitle] sub=[$exSub] mode=$modeShown job=[$($exJob.Title)]"
$gf.Close(); $gf.Dispose()
Check 'תמלול: ״למחוק את הקיימות״ נקרא נכון אחרי שהחלון נסגר' ($rOn -eq $true -and $rOff -eq $false) "on=$rOn off=$rOff"

# שורת הסבר ריקה בכלי לא נשארת כחור - גם כשהיא ריקה כבר כשהחלון נבנה (GIF של קטע קצר; עד 0.8.40 הבדיקה קראה Visible, שלפני
# ההצגה תמיד false, והשורה נשארה)
$tmp4 = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $tmp4)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }
$SNP = [Reflection.BindingFlags]'NonPublic,Public,Static'
$gmi = $asm.GetType('SubtitleStudio.Ff').GetMethod('ProbeFile', $SNP).Invoke($null, @([string]$tmp4))
$gifTool = $null; foreach ($mt in $asm.GetType('SubtitleStudio.MediaTools').GetMethod('All', $SNP).Invoke($null, @())) { if ($mt.Name -like '*GIF*') { $gifTool = $mt; break } }   # לא דרך צינור: הוא עוטף ב-PSObject והבנאי לא נמצא
$ga = New-Object object[] 6; $ga[0] = $null; $ga[1] = $gifTool; $ga[2] = $gmi; $ga[3] = [long]1000; $ga[4] = [long]4000; $ga[5] = [long]0
$gd = [Activator]::CreateInstance($asm.GetType('SubtitleStudio.ToolRunDlg'), [Reflection.BindingFlags]'NonPublic,Public,Instance,CreateInstance', $null, $ga, $null)
$gh = $gd.GetType().GetField('_hintLbl', $NP).GetValue($gd)
$ghSelf = $asm.GetType('SubtitleStudio.Dlg').GetMethod('SelfVisible', $SNP).Invoke($null, @($gh)); $ghText = $gh.Text; $gd.Dispose()
Check 'כלי: שורת הסבר ריקה מוסתרת כבר כשהחלון נבנה (GIF של 3 שניות)' ($ghSelf -eq ($ghText.Length -gt 0)) "visible=$ghSelf text=[$ghText]"

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
