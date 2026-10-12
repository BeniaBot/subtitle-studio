# **השיחה כולה, מול גוגל מדומה** (0.8.12): חלון השיחה והתוכנה האמיתיים, מחוץ למסך, ושרת מקומי שעונה בתשובות כתובות
# מראש ושומר כל בקשה. כך נבדק מה שהשתנה בעוזר בלי לשרוף מכסה - ונבדק בדיוק במקומות שבהם הניסוי האמיתי נפל:
#  1. שתי פעולות בתשובה אחת, עם חתימות: שתיהן מתבצעות, והבקשה הבאה מחזירה את החתימות ותשובה לכל אחת
#  2. תשובה ריקה אחרי תוצאה של פעולה: תזכורת אחת, ואז התשובה
#  3. שאלה עם תשובות לבחירה: השיחה עוצרת, הלחיצה חוזרת כתוצאה
#  4. פקודה שפותחת חלון: המילוי והלחיצה רצים בתוך החלון, והחלון מצורף לתוצאה
#  5. ״לבטל את מה שעשיתי״
#  6. עצירה באמצע
#  7-8. כל הדגמים עמוסים (ממתין וממשיך לבד) / המכסה היומית נגמרה (אומר מתי מתאפס)
# צפוי: 19 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class CfDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][CfDpi]::SetProcessDPIAware()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'; $IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]1.25)

$work = [IO.Path]::GetFullPath((Join-Path $env:TEMP ('ss-chat-' + [guid]::NewGuid().ToString('N').Substring(0, 6))))
New-Item -ItemType Directory $work | Out-Null

# ---------------- גוגל מדומה: תור תשובות, כל בקשה נשמרת ----------------
$server = {
    param($port, $responses, $logDir)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    try {
        for ($k = 0; $k -lt $responses.Count; $k++) {
            $resp = $responses[$k]
            $c = $l.AcceptTcpClient()
            $ns = $c.GetStream(); $ns.ReadTimeout = 15000
            $ms = New-Object IO.MemoryStream; $buf = New-Object byte[] 65536; $headEnd = -1; $clen = 0
            while ($true) {
                $n = $ns.Read($buf, 0, $buf.Length); if ($n -le 0) { break }
                $ms.Write($buf, 0, $n)
                $txt = [Text.Encoding]::ASCII.GetString($ms.ToArray())
                if ($headEnd -lt 0) { $headEnd = $txt.IndexOf("`r`n`r`n"); if ($headEnd -ge 0) { $m = [regex]::Match($txt.Substring(0, $headEnd), '(?im)^content-length:\s*(\d+)'); if ($m.Success) { $clen = [int]$m.Groups[1].Value } } }
                if ($headEnd -ge 0 -and $ms.Length -ge $headEnd + 4 + $clen) { break }
            }
            $raw = $ms.ToArray()
            [IO.File]::WriteAllBytes((Join-Path $logDir ("req-$k.bin")), $raw)
            if ($resp.hold) { $until = [DateTime]::UtcNow.AddSeconds($resp.hold); while ([DateTime]::UtcNow -lt $until) { if ($c.Client.Poll(200000, [Net.Sockets.SelectMode]::SelectRead) -and $c.Client.Available -eq 0) { break } } }
            $body = [Text.Encoding]::UTF8.GetBytes($resp.body)
            $head = "HTTP/1.1 " + $resp.status + " X`r`nContent-Type: application/json`r`nContent-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n"
            $hb = [Text.Encoding]::ASCII.GetBytes($head)
            try { $ns.Write($hb, 0, $hb.Length); $ns.Write($body, 0, $body.Length); $ns.Flush() } catch { }
            $c.Close()
        }
    } finally { $l.Stop() }
}
function FreePort { $t = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), 0; $t.Start(); $p = $t.LocalEndpoint.Port; $t.Stop(); return $p }
function StartServer($responses) {
    $port = FreePort
    $dir = Join-Path $work ('srv-' + $port); New-Item -ItemType Directory $dir | Out-Null
    $ps = [powershell]::Create(); [void]$ps.AddScript($server).AddArgument($port).AddArgument($responses).AddArgument($dir)
    $h = $ps.BeginInvoke(); Start-Sleep -Milliseconds 300
    (TY 'Ai').GetField('EndpointRoot', $ST).SetValue($null, "http://127.0.0.1:$port/v1beta/models/")
    return @{ Port = $port; Ps = $ps; Handle = $h; Dir = $dir }
}
function Req($srv, $k) { $f = Join-Path $srv.Dir "req-$k.bin"; if (-not (Test-Path $f)) { return '' }; $b = [IO.File]::ReadAllBytes($f); $s = [Text.Encoding]::UTF8.GetString($b); $i = $s.IndexOf("`r`n`r`n"); return $s.Substring($i + 4) }
function Ok200($json) { return @{ status = '200 OK'; body = $json } }
function Reply($partsJson) { return Ok200('{"candidates":[{"content":{"role":"model","parts":[' + $partsJson + ']},"finishReason":"STOP"}]}') }
function Call($name, $argsJson, $sig) { $s = '{"functionCall":{"name":"' + $name + '","args":' + $argsJson + '}'; if ($sig) { $s += ',"thoughtSignature":"' + $sig + '"' }; return $s + '}' }
function Txt($t) { return '{"text":"' + $t + '"}' }

# ---------------- התוכנה והצ׳אט, מחוץ למסך ----------------
(TY 'Ai').GetField('Key', $ST).SetValue($null, 'test-key')
(TY 'AiPerms').GetMethod('SetAll', $ST).Invoke($null, @([Enum]::ToObject((TY 'Perm'), 0)))
$mf = [Activator]::CreateInstance((TY 'MainForm'))
$mf.StartPosition = 'Manual'; $mf.Location = New-Object Drawing.Point -7000, -7000; $mf.Size = New-Object Drawing.Size 1400, 900
[void]$mf.Handle
$mfT = TY 'MainForm'
$doc = $mfT.GetField('_doc', $IN).GetValue($mf)
function Cues { return ,((TY 'Doc').GetField('Cues', $IN).GetValue($mfT.GetField('_doc', $IN).GetValue($mf))) }
foreach ($c in @(@(1000, 2500, 'ברוכים הבאים'), @(3000, 5000, 'היום נלמד'))) { (Cues).Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
$chat = NewOf 'AiChatForm' @($mf)
$chat.StartPosition = 'Manual'; $chat.Location = New-Object Drawing.Point -7000, -6000
[void]$chat.Handle
$mfT.GetField('_chat', $IN).SetValue($mf, $chat)
$mfT.GetField('AiConfirmHook', $ST).SetValue($null, [Func[string, bool]] { param($w) return $true })
$chT = TY 'AiChatForm'
$fInput = $chT.GetField('_input', $IN); $fBusy = $chT.GetField('_busy', $IN); $fAsk = $chT.GetField('_ask', $IN); $fHist = $chT.GetField('_history', $IN)

# חלון שנשאר פתוח יותר מ-20 שניות - נסגר (אחרת ShowDialog תוקע את הבדיקה)
# חריגה בתוך הצ׳אט - מודפסת, ולא חלון ״Unhandled exception״ שתוקע את הבדיקה
$script:crashes = New-Object Collections.ArrayList
[Windows.Forms.Application]::add_ThreadException({ param($s, $ev) [void]$script:crashes.Add($ev.Exception.ToString()); Write-Host ('    !! חריגה: ' + $ev.Exception.GetType().Name + ': ' + $ev.Exception.Message) -ForegroundColor Red; Write-Host ($ev.Exception.StackTrace -split "
" | Select-Object -First 4 | Out-String) })
$script:winSince = $null
$guard = New-Object Windows.Forms.Timer; $guard.Interval = 500
$guard.Add_Tick({
    $w = (TY 'AiUi').GetMethod('Current', $ST).Invoke($null, @($mf, $chat))
    if (-not $w) { # חריגה בתוך הצ׳אט - מודפסת, ולא חלון ״Unhandled exception״ שתוקע את הבדיקה
$script:crashes = New-Object Collections.ArrayList
[Windows.Forms.Application]::add_ThreadException({ param($s, $ev) [void]$script:crashes.Add($ev.Exception.ToString()); Write-Host ('    !! חריגה: ' + $ev.Exception.GetType().Name + ': ' + $ev.Exception.Message) -ForegroundColor Red; Write-Host ($ev.Exception.StackTrace -split "
" | Select-Object -First 4 | Out-String) })
$script:winSince = $null; return }
    if (-not $script:winSince) { $script:winSince = Get-Date }
    elseif (((Get-Date) - $script:winSince).TotalSeconds -gt 20) { Write-Host "    (חלון נסגר בידי הבדיקה: $($w.Text))"; $w.Close(); # חריגה בתוך הצ׳אט - מודפסת, ולא חלון ״Unhandled exception״ שתוקע את הבדיקה
$script:crashes = New-Object Collections.ArrayList
[Windows.Forms.Application]::add_ThreadException({ param($s, $ev) [void]$script:crashes.Add($ev.Exception.ToString()); Write-Host ('    !! חריגה: ' + $ev.Exception.GetType().Name + ': ' + $ev.Exception.Message) -ForegroundColor Red; Write-Host ($ev.Exception.StackTrace -split "
" | Select-Object -First 4 | Out-String) })
$script:winSince = $null }
})
$guard.Start()

function Say($text, $timeout = 30) {
    $fInput.GetValue($chat).Text = $text
    $chT.GetMethod('Send', $IN).Invoke($chat, @())
    Pump $timeout
}
function Pump($timeout = 30) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds 50
    while (($fBusy.GetValue($chat)) -and $sw.Elapsed.TotalSeconds -lt $timeout) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
    for ($k = 0; $k -lt 15; $k++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
}
function LastModelText { $h = $fHist.GetValue($chat); for ($i = $h.Count - 1; $i -ge 0; $i--) { $t = (TY 'AiMsg').GetField('Text').GetValue($h[$i]); if ((TY 'AiMsg').GetField('Role').GetValue($h[$i]) -eq 'model' -and $t) { return $t } }; return '' }
function CueText($i) { (TY 'Cue').GetField('Text').GetValue((Cues)[$i]) }
function CueStart($i) { (TY 'Cue').GetField('Start').GetValue((Cues)[$i]) }

# ---------- 1. שתי פעולות בתשובה אחת, עם חתימות ----------
$srv = StartServer @(
    (Reply ((Txt 'מוסיף ומזיז.') + ',' + (Call 'add_cue' '{"start_sec":6,"end_sec":8,"text":"תודה שצפיתם"}' 'SIG-A') + ',' + (Call 'shift_cues' '{"seconds":0.5}' 'SIG-B'))),
    (Reply (Txt 'הוספתי והזזתי.')))
Say 'תוסיף כתובית ואז תזיז הכול'
Check 'שתי הפעולות מתבצעות (עד 0.8.12 רק האחרונה)' ((Cues).Count -eq 3 -and (CueStart 0) -eq 1500) ("" + (Cues).Count + " כתוביות, הראשונה ב-" + (CueStart 0))
$r1 = Req $srv 1
Check 'הבקשה הבאה מחזירה את שתי החתימות' ($r1.Contains('SIG-A') -and $r1.Contains('SIG-B')) ''
Check 'ותשובה לכל פעולה' (([regex]::Matches($r1, '"functionResponse"')).Count -eq 2) ''
Check 'התשובה הסופית מגיעה' ((LastModelText) -eq 'הוספתי והזזתי.') (LastModelText)

# ---------- 5. ״לבטל את מה שעשיתי״ (על מה שקרה ב-1) ----------
$view = $chT.GetField('_view', $IN).GetValue($chat)
$chipItem = $view.GetType().GetField('_chipItem', $IN).GetValue($view)
$chips = if ($chipItem) { $chipItem.GetType().GetField('Chips').GetValue($chipItem) } else { @() }
Check 'אחרי שינוי - מופיע ״לבטל את מה שעשיתי״' (@($chips | Where-Object { $_ -like '*לבטל את מה שעשיתי*' }).Count -eq 1) ($chips -join ' | ')
$chT.GetMethod('UndoRequest', $IN).Invoke($chat, @())
Check 'והלחיצה מחזירה את הכתוביות למה שהיה' ((Cues).Count -eq 2 -and (CueStart 0) -eq 1000) ("" + (Cues).Count)

# ---------- 2. תשובה ריקה אחרי פעולה - תזכורת אחת ----------
$srv = StartServer @(
    (Reply (Call 'list_cues' '{}' $null)),
    (Ok200 '{"candidates":[{"content":{"role":"model","parts":[]},"finishReason":"STOP"}]}'),
    (Reply (Txt 'יש שתי כתוביות.')))
Say 'כמה כתוביות יש?'
$r2 = Req $srv 2
Check 'אחרי תשובה ריקה - הבקשה הבאה מזכירה להמשיך' ($r2.Contains('לא כתבת כלום')) ''
Check 'ואז מגיעה התשובה, בלי שגיאה למשתמש' ((LastModelText) -eq 'יש שתי כתוביות.') (LastModelText)

# ---------- 3. שאלה עם תשובות לבחירה ----------
$srv = StartServer @(
    (Reply (Call 'ask_user' '{"question":"באיזה שירות לתמלל?","options":"גוגל|Groq"}' $null)),
    (Reply (Txt 'בחרת בגוגל.')))
Say 'אני רוצה כתוביות לסרט'
Check 'השיחה עוצרת ומחכה לבחירה' ((-not $fBusy.GetValue($chat)) -and $fAsk.GetValue($chat) -ne $null) ''
$chipItem = $view.GetType().GetField('_chipItem', $IN).GetValue($view)
$chips = if ($chipItem) { $chipItem.GetType().GetField('Chips').GetValue($chipItem) } else { @() }
Check 'התשובות מוצגות כלחצנים' (($chips -join '|') -eq 'גוגל|Groq') ($chips -join ' | ')
$chT.GetMethod('AnswerAsk', $IN, $null, [Type[]]@([string]), $null).Invoke($chat, @('גוגל'))
Pump
$r3 = Req $srv 1
Check 'הבחירה חוזרת למודל כתוצאה של השאלה' ($r3.Contains('"answer":"גוגל"') -or $r3.Contains('"answer":"גוגל"')) ''
Check 'והשיחה ממשיכה' ((LastModelText) -eq 'בחרת בגוגל.') (LastModelText)

# ---------- 4. פקודה שפותחת חלון, מילוי ולחיצה בתוכו ----------
$style = $mfT.GetField('_style', $IN).GetValue($mf)
(TY 'SubStyle').GetField('OpaqueBox').SetValue($style, $false)
$srv = StartServer @(
    (Reply (Call 'run_command' '{"name":"עיצוב הכתוביות"}' $null)),
    (Reply ((Call 'ui_set' '{"control":"רקע מלא","value":"on"}' $null) + ',' + (Call 'ui_click' '{"control":"שמירת העיצוב"}' $null))),
    (Reply (Txt 'הדלקתי רקע מלא.')))
Say 'תשים רקע כהה מאחורי הטקסט' 40
$r4 = Req $srv 1
Check 'החלון שנפתח מצורף לתוצאה של הפקודה (window_now)' ($r4.Contains('window_now') -and ($r4.Contains('שמירת העיצוב') -or $r4.Contains('שמירת'))) ''
$style = $mfT.GetField('_style', $IN).GetValue($mf)
Check 'המילוי והלחיצה רצו בתוך החלון - העיצוב נשמר עם רקע מלא' ((TY 'SubStyle').GetField('OpaqueBox').GetValue($style)) ''
Check 'החלון נסגר בלחיצה' ((TY 'AiUi').GetMethod('Current', $ST).Invoke($null, @($mf, $chat)) -eq $null) ''

# ---------- 6. עצירה באמצע ----------
$srv = StartServer @(@{ status = '200 OK'; body = '{}'; hold = 20 })
$fInput.GetValue($chat).Text = 'משהו ארוך'
$chT.GetMethod('Send', $IN).Invoke($chat, @())
for ($k = 0; $k -lt 25; $k++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 }
$sw = [Diagnostics.Stopwatch]::StartNew()
$chT.GetMethod('StopRequest', $IN).Invoke($chat, @())
Pump 10
Check 'עצירה: השיחה פנויה תוך שניות, לא אחרי שהשרת עונה' ((-not $fBusy.GetValue($chat)) -and $sw.Elapsed.TotalSeconds -lt 5) ("" + [Math]::Round($sw.Elapsed.TotalSeconds, 1) + " שניות")

# ---------- 6א. ״תבטל את מה שעשית״ במילים - הבקשה הקודמת כולה ----------
$before = CueText 0
$srv = StartServer @(
    (Reply (Call 'edit_cue' '{"index":1,"text":"שונה בבקשה"}' $null)),
    (Reply (Txt 'שיניתי.')),
    (Reply (Call 'undo_last_request' '{}' $null)),
    (Reply (Txt 'ביטלתי.')))
Say 'תשנה את הראשונה'
$mid = CueText 0
Say 'תבטל את מה שעשית'
Check 'undo_last_request: הכתובית חוזרת למה שהייתה לפני הבקשה הקודמת' ($mid -eq 'שונה בבקשה' -and (CueText 0) -eq $before) ("$mid ← " + (CueText 0))

# ---------- 7. כל הדגמים עמוסים לרגע (מכסה דקתית): ממתין וממשיך לבד ----------
$models = (TY 'Ai').GetField('Models', $ST).GetValue($null)
$busy429 = @{ status = '429 Too Many Requests'; body = '{"error":{"code":429,"message":"quota","details":[{"retryDelay":"1s"}]}}' }
$seq = @(); foreach ($x in $models) { $seq += $busy429 }; $seq += (Reply (Txt 'אחרי המתנה - הנה.'))
$srv = StartServer $seq
$sw = [Diagnostics.Stopwatch]::StartNew()
Say 'שאלה כשהכול עמוס' 40
$acts = @($view.GetType().GetField('_items', $IN).GetValue($view) | Where-Object { $_.GetType().GetField('Text').GetValue($_) -like '*עמוס*' })
Check 'כולם עמוסים - ״גוגל עמוס רגע״, המתנה, והמשך לבד (בלי ללחוץ ״לנסות שוב״)' ($acts.Count -ge 1 -and (LastModelText) -eq 'אחרי המתנה - הנה.') ("" + [Math]::Round($sw.Elapsed.TotalSeconds) + " שניות")

# ---------- 8. המכסה היומית נגמרה בכל הדגמים: ההודעה אומרת מתי מתאפס ----------
$daily = @{ status = '429 Too Many Requests'; body = '{"error":{"code":429,"message":"GenerateRequestsPerDayPerProjectPerModel exceeded"}}' }
$seq = @(); foreach ($x in $models) { $seq += $daily }
$srv = StartServer $seq
Say 'שאלה כשהמכסה נגמרה' 30
$items = @($view.GetType().GetField('_items', $IN).GetValue($view))
$lastTxt = $items[$items.Count - 1].GetType().GetField('Text').GetValue($items[$items.Count - 1])
Check 'מכסה יומית בכולם - ההודעה אומרת מתי היא מתאפסת' ($lastTxt.Contains('מתאפסת') -and ($lastTxt.Contains('בשעה'))) ($lastTxt -replace "`r?`n", ' ')

$guard.Stop()
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
