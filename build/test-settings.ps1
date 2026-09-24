# שמירת ההגדרות: מה שנשמר באמת נשמר, מפתח לא נמחק על ידי חלון אחר, וכישלון לא נבלע.
#
# **למה:** ב-23.9.2026 בנימין לחץ ״שמירה״ על המפתח של גוגל פעמיים, והמפתח לא הגיע
# לקובץ. לא שוחזר כישלון בדרך עצמה, אבל נמצאו שלושה חורים: בדיקת חיבור מוצלחת
# השאירה מפתח בזיכרון בלי לשמור; כל שגיאת שמירה נבלעה ב-catch ריק; וכל תהליך כותב
# את כל הקובץ, כך שחלון שני שלא הכיר את המפתח מחק אותו בשמירה הבאה שלו.
#
# הכול על קובץ זמני (Settings.FileOverride) - ההגדרות של המשתמש לא נגעות.
# צפוי: 9 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Security
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }

$S = TY 'Settings'; $A = TY 'Ai'
$work = Join-Path $env:TEMP ('ss-settings-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory $work | Out-Null
$ini = Join-Path $work 'settings.ini'
$S.GetField('FileOverride', $ST).SetValue($null, [string]$ini)
$realIni = Join-Path $env:APPDATA 'SubtitleStudio\settings.ini'
$realHash = if (Test-Path $realIni) { (Get-FileHash $realIni).Hash } else { '' }

function Protect([string]$k) { return [string]$A.GetMethod('Protect', $ST).Invoke($null, @($k)) }
function Unprotect([string]$v) { return [string]$A.GetMethod('Unprotect', $ST).Invoke($null, @($v)) }
function DiskKey($name) { $l = Get-Content $ini -Encoding UTF8 | Where-Object { $_ -like "$name=*" }; if ($l) { return $l.Substring($name.Length + 1) } else { return '' } }
function SetKey($v) { $A.GetField('Key', $ST).SetValue($null, [string]$v) }
function Touched($v) { $S.GetField('AiKeyTouched', $ST).SetValue($null, [bool]$v) }
function SaveAll { [void]$S.GetMethod('SaveAll', $ST).Invoke($null, @()) }

# 1. שמירה רגילה, והבדיקה שהחלון עושה אחריה
SetKey 'KEY-ONE-1234567890'; Touched $true; SaveAll
Check 'מפתח שהוזן נשמר לקובץ' ((Unprotect (DiskKey 'aikey')) -eq 'KEY-ONE-1234567890') ''
Check 'KeyOnDisk מזהה אותו' ([bool]$S.GetMethod('KeyOnDisk', $ST).Invoke($null, @([string]'aikey'))) ''

# 2. חלון שני: לא הכיר מפתח ולא נגע בו - ושומר בגלל משהו אחר (עוצמת קול, קובץ אחרון)
SetKey ''; Touched $false
$S.GetField('Volume', $ST).SetValue($null, [int]55); SaveAll
Check 'תהליך שלא הכיר את המפתח לא מוחק אותו' ((Unprotect (DiskKey 'aikey')) -eq 'KEY-ONE-1234567890') ('aikey len ' + (DiskKey 'aikey').Length)
Check 'ושאר מה ששמר - נשמר' ((Get-Content $ini -Encoding UTF8) -contains 'volume=55') ''

# 3. מחיקה מכוונת בחלון המפתח כן מוחקת
SetKey ''; Touched $true; SaveAll
Check 'מפתח שנמחק בכוונה - נמחק' ((DiskKey 'aikey') -eq '') ''

# 4. כישלון שמירה לא נבלע: קובץ לקריאה בלבד
SetKey 'KEY-TWO-1234567890'; Touched $true
(Get-Item $ini).IsReadOnly = $true
SaveAll
(Get-Item $ini).IsReadOnly = $false
$err = [string]$S.GetField('LastError', $ST).GetValue($null)
Check 'שמירה שנכשלה משאירה סיבה (LastError)' ($err.Length -gt 0) $err
Check 'והמפתח לא בקובץ - בדיוק מה שחלון המפתח בודק' (-not [bool]$S.GetMethod('KeyOnDisk', $ST).Invoke($null, @([string]'aikey'))) ''
$log = Join-Path $env:TEMP 'SubStudio-test\ai-log.txt'
Check 'והכישלון נרשם ביומן (של הבדיקות, לא של המשתמש)' ((Test-Path $log) -and ((Get-Content $log -Encoding UTF8 -Tail 5) -join ' ') -match 'שמירת ההגדרות נכשלה') $log

# 4א. שפת הממשק (0.8.1). ‏0.8.0 לא כתבה שורת שפה - וקובץ כזה חייב להישאר בעברית, אחרת
# מי שווינדוס שלו באנגלית היה מעדכן ומקבל פתאום אנגלית
$peek = $S.GetMethod('PeekLang', $ST)
[IO.File]::WriteAllText($ini, "dark=1`r`naikey=`r`nvolume=80`r`n", (New-Object Text.UTF8Encoding $true))
Check 'קובץ של גרסה קודמת (בלי שורת שפה): עברית' ($peek.Invoke($null, @()) -eq 'he') ([string]$peek.Invoke($null, @()))
[IO.File]::Delete($ini)
Check 'אין קובץ בכלל (התקנה חדשה): לזהות לפי המחשב' ($null -eq $peek.Invoke($null, @())) ''
$S.GetField('LangChoice', $ST).SetValue($null, [string]'en'); SaveAll
Check 'בחירה בהגדרות נשמרת, ונקראת בהפעלה הבאה' (((Get-Content $ini -Encoding UTF8) -contains 'lang=en') -and $peek.Invoke($null, @()) -eq 'en') ''
$S.GetField('LangChoice', $ST).SetValue($null, $null)

# 5. וההגדרות האמיתיות לא נגעו
$after = if (Test-Path $realIni) { (Get-FileHash $realIni).Hash } else { '' }
Check 'קובץ ההגדרות של המשתמש לא השתנה' ($after -eq $realHash) ''

$S.GetField('FileOverride', $ST).SetValue($null, $null)
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
