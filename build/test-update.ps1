# בודק שמנגנון העדכון מוצא את המהדורה בגיטהאב וקורא אותה נכון
$exe = "D:\Claude\subtitle-studio\dist\SubtitleStudio.exe"
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$BF = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
function T($n) { $asm.GetType("SubtitleStudio.$n") }

$app = T "App"
$cur = $app.GetField("Version", $BF).GetValue($null)
"גרסה מותקנת: $cur"
"מאגר:        " + $app.GetField("Repo", $BF).GetValue($null)

$up = T "Updater"
$check = $up.GetMethod("Check", $BF)
$args2 = New-Object object[] 1
$rel = $check.Invoke($null, $args2)
if ($args2[0]) { "שגיאה: " + $args2[0] }
if (-not $rel) { "לא נמצאה מהדורה"; exit 1 }

$rt = T "Updater+Release"
if (-not $rt) { $rt = $up.GetNestedType("Release", $BF) }
$ver  = $rt.GetField("Version").GetValue($rel)
$url  = $rt.GetField("Url").GetValue($rel)
$size = $rt.GetField("Size").GetValue($rel)
$notes = $rt.GetField("Notes").GetValue($rel)

"מהדורה בשרת: $ver"
"קובץ:         $url"
"גודל:         {0:N1} MB" -f ($size / 1MB)
"הערות:        " + $notes.Substring(0, [Math]::Min(60, $notes.Length)).Replace("`n", " ") + "..."

$isNewer = $app.GetMethod("IsNewer", $BF).Invoke($null, @([string]$ver))
"האם להציע עדכון: $isNewer"

# הבדיקה נעלה פעם את הגרסה על "v0.1.0" ולכן נכשלה מאז 0.2.0. משווים
# מול הגרסה שבנויה בפועל, כדי שהיא תישאר נכונה בכל מהדורה.
$mine = $app.GetField("Version").GetValue($null)
$ok = $url -like "*.exe" -and $size -gt 10MB -and ($ver -match '^\d+\.\d+\.\d+$')

# שלושה מצבים, וכולם תקינים - רק ההכרעה צריכה להתאים למציאות.
# לפני שחרור הגרסה הבנויה מקדימה את השרת, וזה בדיוק המצב כאן.
function Cmp($a, $b) {
    $x = $a.Split('.'); $y = $b.Split('.')
    for ($i = 0; $i -lt 3; $i++) {
        $u = [int]$x[$i]; $v = [int]$y[$i]
        if ($u -ne $v) { if ($u -gt $v) { return 1 } else { return -1 } }
    }
    return 0
}
$rel = Cmp $ver $mine
if ($rel -gt 0)  { $ok = $ok -and $isNewer;        "השרת מקדים - חייב להציע" }
elseif ($rel -eq 0) { $ok = $ok -and (-not $isNewer); "אותה גרסה - אין מה להציע" }
else { $ok = $ok -and (-not $isNewer); "הגרסה הבנויה מקדימה את השרת (עוד לא שוחררה) - אין מה להציע" }
if ($ok) { Write-Host "`nOK - מנגנון העדכון קורא את המהדורה נכון" -ForegroundColor Green }
else { Write-Host "`nFAIL" -ForegroundColor Red; exit 1 }
