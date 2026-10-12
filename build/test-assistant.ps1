# העוזר בלי רשת (0.8.12): קריאת תשובות של גוגל (כמה פעולות, חתימות, חשיבה, ריקה), שליחה חזרה כמו שהגיעו,
# ההרשאות (מותר / לשאול / אסור), הפקודות, עבודה בחלון (AiUi), ״לבטל את מה שעשיתי״, המצב, המדריך, הראייה והקול.
# **למה:** בניסוי עם מפתח אמיתי (11.10) בקשה של שני שלבים נפלה ב״missing a thought_signature״, ורק הפעולה האחרונה
# מתוך כמה בוצעה. את הניסוי עצמו אי אפשר להריץ בכל בנייה (מכסה), אז מה שנמצא בו נבדק כאן.
# צפוי: 56 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
Add-Type @'
using System.Runtime.InteropServices;
public static class AsDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][AsDpi]::SetProcessDPIAware()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'; $IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }; return ,$a }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]1.25)
$ser = New-Object Web.Script.Serialization.JavaScriptSerializer
$ai = TY 'Ai'; $replyT = TY 'AiReply'; $msgT = TY 'AiMsg'; $callT = TY 'AiCall'

# ---------- 1. קריאת תשובה: כמה פעולות, חתימה, חשיבה ----------
$json = '{"candidates":[{"content":{"role":"model","parts":[{"text":"מסכם","thought":true},{"text":"מוסיף ומזיז."},{"functionCall":{"name":"add_cue","args":{"start_sec":9,"end_sec":11,"text":"תודה"}},"thoughtSignature":"SIG-A"},{"functionCall":{"name":"shift_cues","args":{"seconds":-0.5}},"thoughtSignature":"SIG-B"}]},"finishReason":"STOP"}]}'
$r = [Activator]::CreateInstance($replyT)
$ai.GetMethod('ParseReply', $ST).Invoke($null, (Pack $r $json))
$calls = $replyT.GetField('Calls').GetValue($r); $parts = $replyT.GetField('Parts').GetValue($r)
Check 'שתי בקשות בתשובה אחת - שתיהן נקראות (עד 0.8.12 רק האחרונה)' ($calls.Count -eq 2 -and $callT.GetField('Name').GetValue($calls[0]) -eq 'add_cue' -and $callT.GetField('Name').GetValue($calls[1]) -eq 'shift_cues') ("" + $calls.Count)
Check 'סיכום החשיבה לא נכנס לטקסט למשתמש' ($replyT.GetField('Text').GetValue($r) -eq 'מוסיף ומזיז.') $replyT.GetField('Text').GetValue($r)
Check 'כל החלקים נשמרים כמו שהם (4)' ($parts.Count -eq 4) ("" + $parts.Count)

# ---------- 2. שליחה חזרה: התור של המודל כמו שהגיע, תשובה לכל בקשה ----------
$listT = [Collections.Generic.List``1].MakeGenericType($msgT); $hist = [Activator]::CreateInstance($listT)
$u = [Activator]::CreateInstance($msgT); $msgT.GetField('Text').SetValue($u, 'תוסיף ותזיז'); $hist.Add($u)
$m = [Activator]::CreateInstance($msgT); $msgT.GetField('Role').SetValue($m, 'model'); $msgT.GetField('Parts').SetValue($m, $parts); $hist.Add($m)
$resT = [Collections.Generic.List``1].MakeGenericType([Collections.Generic.Dictionary[string, object]]); $results = [Activator]::CreateInstance($resT)
foreach ($k in 1..2) { $d = New-Object 'Collections.Generic.Dictionary[string,object]'; $d['done'] = "ok$k"; $results.Add($d) }
$t = [Activator]::CreateInstance($msgT); $msgT.GetField('Role').SetValue($t, 'tool'); $msgT.GetField('Calls').SetValue($t, $calls); $msgT.GetField('Results').SetValue($t, $results); $hist.Add($t)
$body = $ai.GetMethod('BuildBody', $ST).Invoke($null, (Pack 'sys' $hist $null $false 'gemini-flash-latest'))
$b = $ser.DeserializeObject($body); $cont = $b['contents']
Check 'החתימות חוזרות לשרת (thoughtSignature בשתי הבקשות)' ($body.Contains('SIG-A') -and $body.Contains('SIG-B')) ''
Check 'תור המודל אחד, עם כל החלקים' ($cont.Count -eq 3 -and $cont[1]['role'] -eq 'model' -and $cont[1]['parts'].Count -eq 4) ("" + $cont.Count)
Check 'תשובה לכל בקשה, באותו תור ובאותו סדר' ($cont[2]['parts'].Count -eq 2 -and $cont[2]['parts'][0]['functionResponse']['name'] -eq 'add_cue' -and $cont[2]['parts'][1]['functionResponse']['response']['done'] -eq 'ok2') ''

# ---------- 3. תשובה ריקה וחסימה ----------
$r2 = [Activator]::CreateInstance($replyT); $ai.GetMethod('ParseReply', $ST).Invoke($null, (Pack $r2 '{"candidates":[{"content":{"role":"model","parts":[]},"finishReason":"STOP"}]}'))
Check 'תשובה ריקה מסומנת Empty (והצ׳אט מזכיר להמשיך)' ($replyT.GetField('Empty').GetValue($r2)) ''
$r3 = [Activator]::CreateInstance($replyT); $ai.GetMethod('ParseReply', $ST).Invoke($null, (Pack $r3 '{"candidates":[{"content":{"role":"model","parts":[]},"finishReason":"MAX_TOKENS"}]}'))
Check 'קיטוע באורך - לא Empty, עם הסבר' (-not $replyT.GetField('Empty').GetValue($r3) -and $replyT.GetField('Error').GetValue($r3)) ''

# ---------- 4. תמונות נשלחות כחלקים ----------
$pm = [Activator]::CreateInstance($msgT); $msgT.GetField('Text').SetValue($pm, 'תמונות')
$il = New-Object 'Collections.Generic.List[string]'; $il.Add('QUJD'); $il.Add('REVG'); $msgT.GetField('Images').SetValue($pm, $il)
$h2 = [Activator]::CreateInstance($listT); $h2.Add($pm)
$b2 = $ser.DeserializeObject($ai.GetMethod('BuildBody', $ST).Invoke($null, (Pack 'sys' $h2 $null $false 'm')))
Check 'הודעה עם תמונות: טקסט ושתי תמונות JPEG' ($b2['contents'][0]['parts'].Count -eq 3 -and $b2['contents'][0]['parts'][1]['inline_data']['mime_type'] -eq 'image/jpeg') ''

# ---------- 5. ההרשאות ----------
$perms = TY 'AiPerms'
$perms.GetMethod('Parse', $ST).Invoke($null, @('2,1,0,1,2'))
Check 'ההרשאות נשמרות ונקראות בחזרה' ($perms.GetMethod('Serialize', $ST).Invoke($null, @()) -eq '2,1,0,1,2') ''
$cat = $perms.GetMethod('CategoryOf', $ST)
Check 'סוג הפעולה: עריכה / קבצים חדשים / שליחה / רק קריאה' ($cat.Invoke($null, @('edit_cue')) -eq 0 -and $cat.Invoke($null, @('export_video')) -eq 1 -and $cat.Invoke($null, @('find_scene')) -eq 3 -and $cat.Invoke($null, @('list_cues')) -eq -1 -and $cat.Invoke($null, @('ask_user')) -eq -1) ''
$sum = $perms.GetMethod('Summary', $ST).Invoke($null, @())
Check 'הסיכום בכרטיס אומר מה מותר, על מה שואל ומה אסור' ($sum.Contains('מותר') -and $sum.Contains('שואל') -and $sum.Contains('אסור')) $sum

# חלון ראשי עם כתוביות
$mf = [Activator]::CreateInstance((TY 'MainForm'))
$mf.StartPosition = 'Manual'; $mf.Location = New-Object Drawing.Point -7000, -7000; $mf.Size = New-Object Drawing.Size 1400, 900
[void]$mf.Handle
$mfT = TY 'MainForm'
$doc = $mfT.GetField('_doc', $IN).GetValue($mf); $cues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
foreach ($c in @(@(1000, 2500, 'ברוכים הבאים'), @(3000, 5000, 'היום נלמד'), @(6000, 8000, 'תודה'))) { $cues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
$run = $mfT.GetMethod('RunAiAction')
$script:asked = New-Object Collections.ArrayList; $script:answer = $true
$mfT.GetField('AiConfirmHook', $ST).SetValue($null, [Func[string, bool]] { param($w) [void]$script:asked.Add($w); return $script:answer })
function Call($name, $argsH) {
    $c = [Activator]::CreateInstance($callT); $callT.GetField('Name').SetValue($c, $name)
    $d = New-Object 'Collections.Generic.Dictionary[string,object]'; foreach ($k in $argsH.Keys) { $d[$k] = $argsH[$k] }; $callT.GetField('Args').SetValue($c, $d)
    $ref = New-Object object[] 2; $ref[0] = $c; $ref[1] = $false
    $res = $run.Invoke($mf, $ref); $script:refused = $ref[1]; return $res
}
function CueText($i) { (TY 'Cue').GetField('Text').GetValue($cues[$i]) }

$perms.GetMethod('Parse', $ST).Invoke($null, @('2,1,1,1,1'))
$res = Call 'edit_cue' @{ index = 1; text = 'שונה' }
Check 'עריכה ״אסור״ - לא מבצע, ואומר איפה מתירים' ((CueText 0) -eq 'ברוכים הבאים' -and ('' + $res['error']).Contains('העוזר: מה מותר לו')) ('' + $res['error'])
$perms.GetMethod('Parse', $ST).Invoke($null, @('1,1,1,1,1')); $script:answer = $false; $script:asked.Clear()
$res = Call 'edit_cue' @{ index = 1; text = 'שונה' }
Check 'עריכה ״לשאול״ ולא אישר - לא מבצע, ומסומן כביטול' ((CueText 0) -eq 'ברוכים הבאים' -and $script:refused -and $script:asked.Count -eq 1) ($script:asked -join ' | ')
$script:answer = $true
$res = Call 'edit_cue' @{ index = 1; text = 'שונה' }
Check 'עריכה ״לשאול״ ואישר - מבצע' ((CueText 0) -eq 'שונה') ('' + $res['done'])
$perms.GetMethod('Parse', $ST).Invoke($null, @('0,0,0,0,0')); $script:asked.Clear()
$res = Call 'edit_cue' @{ index = 1; text = 'ברוכים הבאים' }
Check 'עריכה ״מותר״ - בלי לשאול' ((CueText 0) -eq 'ברוכים הבאים' -and $script:asked.Count -eq 0) ''
$res = Call 'list_cues' @{}
Check 'קריאה תמיד מותרת' ($res['cues'].Length -eq 3) ''

# ---------- 6. המצב ----------
$state = $mfT.GetMethod('AiStateFull', $IN).Invoke($mf, @())
Check 'בלי סרט - המצב אומר כמה כתוביות יש (עד 0.8.12: ״לא פתוח קובץ״, והוא ענה ״אין כתוביות״)' ($state.Contains('3 כתוביות') -and $state.Contains('אין סרט')) $state.Substring(0, [Math]::Min(120, $state.Length))
Check 'המצב אומר אילו שירותי תמלול מחוברים' ($state.Contains('שירותי תמלול')) ''

# ---------- 7. המדריך ----------
$res = Call 'app_help' @{ topic = 'export' }
Check 'app_help: הנושא עם שמות הכפתורים האמיתיים' (('' + $res['text']).Contains('התחלת ההטמעה') -and ('' + $res['text']).Contains('צריבה בתמונה')) ''
$res = Call 'app_help' @{ topic = 'tools' }
Check 'app_help tools: נבנה מהקוד, עם כל הכלים' (('' + $res['text']).Contains('דחיסה')) ''
$res = Call 'app_help' @{ topic = 'אין כזה' }
Check 'נושא לא קיים - מחזיר את רשימת הנושאים' ($res['error'] -and ('' + $res['topics']).Contains('export')) ''
$ok = $true; foreach ($tp in (TY 'AiKnow').GetField('Topics', $ST).GetValue($null)) { if (-not (TY 'AiKnow').GetMethod('Topic', $ST).Invoke($null, (Pack $tp $mf))) { $ok = $false } }
Check 'לכל נושא ברשימה יש טקסט' $ok ''

# ---------- 8. הפקודות ----------
$res = Call 'list_commands' @{}
$names = @($res['commands'] | ForEach-Object { $_['name'] })
Check 'list_commands: התפריטים והסרגל' ($names -contains 'עיצוב הכתוביות' -and $names -contains 'חיתוך קטעים' -and $names -contains 'יצירת סרט עם כתוביות') ("" + $names.Count + " פקודות")
$res = Call 'run_command' @{ name = 'שליפת כתוביות מהסרט' }
Check 'פקודה לא זמינה (אין סרט) - לא מופעלת, ואומר למה' (('' + $res['error']).Contains('לא זמינה')) ('' + $res['error'])
[void]$mfT.GetMethod('AiTakeDeferred').Invoke($mf, @())
$res = Call 'run_command' @{ name = 'עיצוב הכתוביות' }
Check 'פקודה זמינה - נשלחת לתור (נפתחת אחרי שהקריאה חוזרת)' ($res['done'] -and $mfT.GetMethod('AiTakeDeferred').Invoke($mf, @())) ('' + $res['done'])
$res = Call 'run_command' @{ name = 'אין פקודה כזאת בכלל' }
Check 'פקודה לא קיימת - שגיאה ברורה' ('' + $res['error']).Contains('list_commands') ''

# ---------- 9. עבודה בחלון (AiUi) ----------
$styleT = TY 'StyleDlg'
$ctor = $styleT.GetConstructors($IN) | Select-Object -First 1
$sd = $null
try { $sd = $ctor.Invoke((Pack ((TY 'SubStyle').GetConstructor([Type]::EmptyTypes).Invoke(@())))) } catch { }
if (-not $sd) { try { $sd = $ctor.Invoke((Pack ((TY 'SubStyle').GetConstructor([Type]::EmptyTypes).Invoke(@())) $null)) } catch { } }
Check 'חלון העיצוב נבנה לבדיקה' ($sd -ne $null) ''
if ($sd) {
    $items = (TY 'AiUi').GetMethod('Items', $ST).Invoke($null, @($sd))
    $sliders = @($items | Where-Object { $_.Kind -eq 'slider' } | ForEach-Object { $_.Label })
    Check 'שני הפסים שזה ליד זה - כל אחד עם התווית שמעליו (עד כאן שניהם ״מרחק מהקצה״)' (($sliders -contains 'עובי המתאר') -and ($sliders -contains 'מרחק מהקצה')) ($sliders -join ', ')
    $box = (TY 'AiUi').GetMethod('Find', $ST).Invoke($null, (Pack $sd 'רקע מלא'))
    Check 'מוצא פקד לפי חלק מהתווית' ($box -ne $null -and $box.Kind -eq 'switch') ''
    $why = (TY 'AiUi').GetMethod('Set', $ST).Invoke($null, (Pack $box 'on'))
    Check 'מדליק מתג' ($why -eq $null -and (TY 'AiUi').GetMethod('Value', $ST).Invoke($null, @($box)) -eq 'on') ''
    $sl = (TY 'AiUi').GetMethod('Find', $ST).Invoke($null, (Pack $sd 'עובי המתאר'))
    [void](TY 'AiUi').GetMethod('Set', $ST).Invoke($null, (Pack $sl '4'))
    Check 'קובע מספר בפס' ((TY 'AiUi').GetMethod('Value', $ST).Invoke($null, @($sl)) -eq '4') ''
    $why = (TY 'AiUi').GetMethod('Set', $ST).Invoke($null, (Pack $sl 'הרבה'))
    Check 'ערך לא מספרי לפס - הסבר, לא חריגה' ($why -ne $null) $why
    $desc = (TY 'AiUi').GetMethod('Describe', $ST).Invoke($null, @($sd))
    $main = @($desc['controls'] | Where-Object { $_['main'] })
    Check 'תיאור החלון מסמן את הכפתור הראשי' ($main.Count -eq 1 -and $main[0]['label'] -eq 'שמירת העיצוב') ''
    $sd.Dispose()
}

# ---------- 10. ״לבטל את מה שעשיתי״ ----------
$snap = $mfT.GetMethod('AiSnapshot').Invoke($mf, @())
Check 'בלי שינוי - אין כפתור ביטול' (-not $mfT.GetMethod('AiChangedSince').Invoke($mf, @($snap))) ''
[void](Call 'edit_cue' @{ index = 2; text = 'שונה מהעוזר' }); [void](Call 'delete_cues' @{ from_index = 3 })
Check 'אחרי שינויים - יש' ($mfT.GetMethod('AiChangedSince').Invoke($mf, @($snap))) ''
$docNow = $mfT.GetField('_doc', $IN).GetValue($mf)
[void]$mfT.GetMethod('AiRestore').Invoke($mf, @($snap))
$cues = (TY 'Doc').GetField('Cues', $IN).GetValue($docNow)
Check 'חוזר בדיוק למה שהיה (שלוש כתוביות, הטקסט המקורי)' ($cues.Count -eq 3 -and (CueText 1) -eq 'היום נלמד') ("" + $cues.Count)
Check 'והחזרה היא צעד ביטול אחד (Ctrl+Z מחזיר)' ((TY 'Doc').GetProperty('UndoLabel').GetValue($docNow) -eq 'ביטול מה שהעוזר עשה') ''

# ---------- 11. שאלה עם תשובות, טקסט נקי, ראייה, קול ----------
$plain = (TY 'AiChatForm').GetMethod('Plain', $ST)
Check 'בלי מרקדאון בבועה (״**צריבה**״ ← ״צריבה״, ״# כותרת״, ״* פריט״)' ($plain.Invoke($null, @("**צריבה** בתמונה`n# כותרת`n* פריט")) -eq "צריבה בתמונה`nכותרת`n· פריט") ''
$times = (TY 'AiVision').GetMethod('Times', $ST).Invoke($null, (Pack ([int64]0) ([int64]60000) 96 ([int64]2000)))
Check 'זמנים לחיפוש סצנה: לא צפוף משתי שניות' ($times.Count -eq 30 -and $times[0] -eq 1000) ("" + $times.Count)
$fit = (TY 'AiVision').GetMethod('Fit', $ST).Invoke($null, (Pack 1920 1080 384 216))
Check 'תמונה קטנה שומרת על היחס' ($fit.Width -eq 384 -and $fit.Height -eq 216) ''
$wargs = New-Object object[] 2; $wargs[0] = [byte[]]::new(4800); $wargs[1] = 24000
$wav = (TY 'AiVoice').GetMethod('Wav', $ST).Invoke($null, $wargs)
Check 'WAV להקראה: כותרת של 44 בתים' ($wav.Length -eq 4844 -and [Text.Encoding]::ASCII.GetString($wav, 0, 4) -eq 'RIFF') ''
$clean = (TY 'AiVoice').GetMethod('Clean', $ST).Invoke($null, @(('משפט ארוך. ' * 120)))
Check 'הקראה של תשובה ארוכה נעצרת בסוף משפט, עד 700 תווים' ($clean.Length -le 700 -and $clean.EndsWith('.')) ("" + $clean.Length)
$tools = $mfT.GetMethod('AiTools').Invoke($mf, @())
$tn = @($tools | ForEach-Object { $_.Name })
Check 'הפעולות החדשות ברשימה (ask_user, look_at, find_scene, ui_*, run_command, app_help)' ((@('ask_user', 'look_at', 'find_scene', 'ui_read', 'ui_set', 'ui_click', 'run_command', 'list_commands', 'app_help', 'select_cue', 'redo', 'set_setting') | Where-Object { $tn -notcontains $_ }).Count -eq 0) ("" + $tn.Count + " פעולות")
$bodyT = $ai.GetMethod('BuildBody', $ST).Invoke($null, (Pack 'sys' $hist $tools $false 'm'))
Check 'הגדרות הפעולות נבנות לסכמה תקינה' ($ser.DeserializeObject($bodyT)['tools'][0]['functionDeclarations'].Count -eq $tools.Count) ''

$sugg = @($mfT.GetMethod('AiSuggestions').Invoke($mf, @()))
Check 'הצעות לפתיחה בלי סרט ועם כתוביות - לתרגם, לא ״תמלל״' (($sugg -contains 'תרגם את הכתוביות לאנגלית') -and -not ($sugg -contains 'אני רוצה כתוביות לסרט')) ($sugg -join ' | ')

# ---------- 12. עם סרט: להקשיב, להסתכל, למצוא סצנה; הכתוביות במצב; שעת האיפוס ----------
$tmpDir = Join-Path $env:TEMP ('ss-asst-' + [guid]::NewGuid().ToString('N').Substring(0, 6)); New-Item -ItemType Directory $tmpDir | Out-Null
$clip = Join-Path $tmpDir 'clip.mp4'
$ffx = (TY 'Ff').GetProperty('Exe', $ST).GetValue($null)
& $ffx -hide_banner -v error -f lavfi -i 'testsrc=size=320x180:rate=25:duration=6' -f lavfi -i 'sine=frequency=440:duration=6' -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest -y $clip 2>&1 | Out-Null
[void]$mfT.GetMethod('OpenMedia', $IN, $null, [Type[]]@([string]), $null).Invoke($mf, @([string]$clip))
$cues = (TY 'Doc').GetField('Cues', $IN).GetValue($mfT.GetField('_doc', $IN).GetValue($mf))
if ($cues.Count -eq 0) { foreach ($c in @(@(1000, 2500, 'ברוכים הבאים'), @(3000, 5000, 'היום נלמד'))) { $cues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) } }
function RunWork($res) { $fn = $res['_work']; if (-not $fn) { return $null }; return $fn.Invoke() }
$res = Call 'listen' @{ index = 1 }
$w = RunWork $res
Check 'listen: קטע הקול של הכתובית יוצא (MP3)' ($w -ne $null -and $w['_audio'] -ne $null -and $w['_audio'].Length -gt 1000) ($(if ($w) { '' + $w['clip'] + ' ' + $(if ($w['_audio']) { $w['_audio'].Length } else { $w['error'] }) }))
$res = Call 'look_at' @{ seconds = 2 }
$w = RunWork $res
Check 'look_at: תמונה אחת מהסרט' ($w -ne $null -and $w['_images'].Count -eq 1) ''
$res = Call 'find_scene' @{ description = 'מה שהוא' }
$w = RunWork $res
Check 'find_scene: דף תמונות עם זמנים' ($w -ne $null -and $w['_images'].Count -ge 1 -and $w['sheets'] -ge 1) ''
$state = $mfT.GetMethod('AiStateFull', $IN).Invoke($mf, @())
Check 'המצב כולל את הכתוביות עצמן (בלי סבב list_cues)' ($state.Contains('הכתוביות:') -and $state.Contains('ברוכים הבאים')) ''
$qr = (TY 'Ai').GetMethod('QuotaResetText', $ST, $null, [Type[]]@([DateTime]), $null)
Check 'שעת איפוס המכסה: לפני - ״היום״, אחרי - ״מחר״' ($qr.Invoke($null, @([DateTime]'2026-10-12 04:30')).Contains('היום') -and $qr.Invoke($null, @([DateTime]'2026-10-12 11:00')).Contains('מחר')) ''
Check 'listen נחשבת שליחה החוצה' ($cat.Invoke($null, @('listen')) -eq 3) ''
$tn2 = @($mfT.GetMethod('AiTools').Invoke($mf, @()) | ForEach-Object { $_.Name })
Check 'listen ברשימת הפעולות' ($tn2 -contains 'listen') ''
$sugg = @($mfT.GetMethod('AiSuggestions').Invoke($mf, @()))
Check 'הצעות עם סרט וכתוביות - גם ליצור את הסרט' ($sugg -contains 'תיצור סרט עם הכתוביות') ($sugg -join ' | ')
[void]$mfT.GetMethod('AiTakeDeferred').Invoke($mf, @())
$res = Call 'trim_video' @{ sections = '0-2 remove; 0:03-end keep' }
$secs = $mfT.GetField('_cutSections', $IN).GetValue($mf)
Check 'trim_video עם קטעים: נפתח עם הקטעים (הסרה, ושמירה עד הסוף)' ($secs.Count -eq 2 -and -not $secs[0].Keep -and $secs[1].Keep -and $secs[1].A -eq 3000 -and $secs[1].B -ge 5900 -and $mfT.GetMethod('AiTakeDeferred').Invoke($mf, @())) ('' + $res['done'])
$res = Call 'trim_video' @{ sections = 'מההתחלה עד האמצע' }
Check 'קטע שלא מובן - שגיאה עם דוגמה, לא חלון' (('' + $res['error']).Contains('keep')) ('' + $res['error'])
[void]$mfT.GetMethod('AiTakeDeferred').Invoke($mf, @())
try { Remove-Item $tmpDir -Recurse -Force } catch { }

$mfT.GetField('AiConfirmHook', $ST).SetValue($null, $null)
$perms.GetMethod('ResetDefaults', $ST).Invoke($null, @())
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
