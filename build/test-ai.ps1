# בודק את נתיב ה-AI של התוכנה עצמה (בלי ממשק): בונה בקשה אמיתית ושולח.
# עם -Key אפשר לבדוק מפתח אמיתי; בלי מפתח נבדקת רק תקינות הבקשה מול השרת.
param([string]$Key = "INVALID_TEST_KEY", [switch]$WithTools)

$exe = "D:\Claude\subtitle-studio\dist\SubtitleStudio.exe"
if (-not (Test-Path $exe)) { Write-Host "no exe"; exit 1 }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$NP = [Reflection.BindingFlags]::NonPublic
$PB = [Reflection.BindingFlags]::Public
$ST = [Reflection.BindingFlags]::Static
$IN = [Reflection.BindingFlags]::Instance
$CI = [Reflection.BindingFlags]::CreateInstance

function T($n) { $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { [Activator]::CreateInstance((T $n), ($NP -bor $PB -bor $IN -bor $CI), $null, $argv, $null) }
function Pack {
    $a = New-Object object[] $args.Count
    for ($i = 0; $i -lt $args.Count; $i++) { $a[$i] = $args[$i] }
    return ,$a
}

$ai = T "Ai"
$ai.GetField("Key", $NP -bor $PB -bor $ST).SetValue($null, $Key)
"model: " + $ai.GetField("Model", $NP -bor $PB -bor $ST).GetValue($null)

# שיחה של הודעה אחת
$msgT = T "AiMsg"
$msg = [Activator]::CreateInstance($msgT, ($NP -bor $PB -bor $IN -bor $CI), $null, $null, $null)
$msgT.GetField("Text").SetValue($msg, "ענה במילה אחת: שלום")
$listT = [System.Collections.Generic.List``1].MakeGenericType($msgT)
$hist = [Activator]::CreateInstance($listT)
$listT.GetMethod("Add").Invoke($hist, (Pack $msg))

# כלים - רק אם ביקשו
$tools = $null
if ($WithTools) {
    $toolT = T "AiTool"
    $tl = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($toolT))
    $t1 = [Activator]::CreateInstance($toolT, ($NP -bor $PB -bor $IN -bor $CI), $null, (Pack "get_state" "מצב נוכחי"), $null)
    $t2 = [Activator]::CreateInstance($toolT, ($NP -bor $PB -bor $IN -bor $CI), $null, (Pack "shift_cues" "מזיז תזמונים"), $null)
    $req = $toolT.GetMethod("Req")
    [void]$req.Invoke($t2, (Pack "seconds" "number" "כמה שניות"))
    $tl.GetType().GetMethod("Add").Invoke($tl, (Pack $t1))
    $tl.GetType().GetMethod("Add").Invoke($tl, (Pack $t2))
    $tools = $tl
}

$send = $ai.GetMethod("Send", $NP -bor $PB -bor $ST)
$reply = $send.Invoke($null, (Pack "אתה עוזר בתוכנת כתוביות. ענה קצר." $hist $tools $false))
$rt = T "AiReply"
$ok = $rt.GetField("Error").GetValue($reply) -eq $null
"ok:    $ok"
"text:  " + $rt.GetField("Text").GetValue($reply)
"error: " + $rt.GetField("Error").GetValue($reply)
$call = $rt.GetField("Call").GetValue($reply)
if ($call) { "call:  " + (T "AiCall").GetField("Name").GetValue($call) }
