# האם העוזר באמת קורא לפונקציות: בקשה אמיתית עם כל הכלים והפרומפט האמיתי.
param([string]$Key = $env:GEMKEY, [string]$Model = "", [string]$Prompt = "תקרא את הכתוביות")
$ErrorActionPreference = 'Continue'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$exe = 'D:\Claude\subtitle-studio\dist\SubtitleStudio.exe'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$NP=[Reflection.BindingFlags]::NonPublic; $PB=[Reflection.BindingFlags]::Public
$ST=[Reflection.BindingFlags]::Static; $IN=[Reflection.BindingFlags]::Instance
$CI=[Reflection.BindingFlags]::CreateInstance
function T($n){ $asm.GetType("SubtitleStudio.$n") }
function Pack { $a=New-Object object[] $args.Count; for($i=0;$i -lt $args.Count;$i++){$a[$i]=$args[$i]}; return ,$a }

$ai = T 'Ai'
$ai.GetField('Key', $NP -bor $PB -bor $ST).SetValue($null, $Key)
if ($Model) { $ai.GetField('Model', $NP -bor $PB -bor $ST).SetValue($null, $Model) }
$using = $ai.GetField('Model', $NP -bor $PB -bor $ST).GetValue($null)

# טופס אמיתי - כדי לקבל את רשימת הפעולות ואת הפרומפט כמו שהם באמת
$mfT = T 'MainForm'
$mf = [Activator]::CreateInstance($mfT, ($NP -bor $PB -bor $IN -bor $CI), $null, $null, $null)
$tools = $mfT.GetMethod('AiTools').Invoke($mf, @())
Write-Host ("דגם: $using   פעולות: " + $tools.Count)

$chatT = T 'AiChatForm'
$chat = [Activator]::CreateInstance($chatT, ($NP -bor $PB -bor $IN -bor $CI), $null, (Pack $mf), $null)
$sys = $chatT.GetMethod('SystemPrompt', $NP -bor $IN).Invoke($chat, @())
Write-Host ("פרומפט מערכת: " + $sys.Length + " תווים")

$msgT = T 'AiMsg'
$msg = [Activator]::CreateInstance($msgT, ($NP -bor $PB -bor $IN -bor $CI), $null, $null, $null)
$msgT.GetField('Text').SetValue($msg, $Prompt)
$listT = [System.Collections.Generic.List``1].MakeGenericType($msgT)
$hist = [Activator]::CreateInstance($listT)
[void]$listT.GetMethod('Add').Invoke($hist, (Pack $msg))

$body = $ai.GetMethod('BuildBody', $NP -bor $PB -bor $ST).Invoke($null, (Pack $sys $hist $tools $false $using))
Write-Host ("גוף הבקשה: " + $body.Length + " תווים; thinkingConfig=" + ($body -match 'thinkingConfig'))

$reply = $ai.GetMethod('Send', $NP -bor $PB -bor $ST).Invoke($null, (Pack $sys $hist $tools $false))
$rt = T 'AiReply'
Write-Host ("--- שאלה: $Prompt")
Write-Host ("finish: " + $rt.GetField('Finish').GetValue($reply))
Write-Host ("error:  " + $rt.GetField('Error').GetValue($reply))
$call = $rt.GetField('Call').GetValue($reply)
if ($call) { Write-Host ("CALL:   " + (T 'AiCall').GetField('Name').GetValue($call)) -ForegroundColor Green }
else { Write-Host "CALL:   (אין - ענה בטקסט בלבד)" -ForegroundColor Yellow }
Write-Host ("text:   " + $rt.GetField('Text').GetValue($reply))
