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
"האם להציע עדכון: $isNewer   (אמור להיות False - זו אותה גרסה)"

$ok = ($ver -eq "v0.1.0" -or $ver -eq "0.1.0") -and $url -like "*.exe" -and $size -gt 10MB -and -not $isNewer
if ($ok) { Write-Host "`nOK - מנגנון העדכון קורא את המהדורה נכון" -ForegroundColor Green }
else { Write-Host "`nFAIL" -ForegroundColor Red; exit 1 }
