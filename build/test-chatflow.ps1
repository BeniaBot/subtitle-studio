# **השיחה כולה, מול גוגל מדומה** (0.8.12): חלון השיחה והתוכנה האמיתיים, מחוץ למסך, ושרת מקומי שעונה בתשובות כתובות
# מראש ושומר כל בקשה. כך נבדק מה שהשתנה בעוזר בלי לשרוף מכסה - ונבדק בדיוק במקומות שבהם הניסוי האמיתי נפל:
#  1. שתי פעולות בתשובה אחת, עם חתימות: שתיהן מתבצעות, והבקשה הבאה מחזירה את החתימות ותשובה לכל אחת
#  2. תשובה ריקה אחרי תוצאה של פעולה: תזכורת אחת, ואז התשובה
#  3. שאלה עם תשובות לבחירה: השיחה עוצרת, הלחיצה חוזרת כתוצאה
#  4. פקודה שפותחת חלון: המילוי והלחיצה רצים בתוך החלון, והחלון מצורף לתוצאה
#  5. ״לבטל את מה שעשיתי״
#  6. עצירה באמצע
#  7-8. כל הדגמים עמוסים (ממתין וממשיך לבד) / המכסה היומית נגמרה (אומר מתי מתאפס)
#  9. גוגל איטי - אותה בקשה גם לדגם אחר, במקביל (0.8.14)
#  10-13. הודעה קולית: המילים בבועה / תמלול נכשל - ההקלטה עצמה / בלי דיבור / עצירה באמצע
# צפוי: 40 בדיקות.
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
            $extra = ""; if ($resp.headers) { $extra = ($resp.headers -replace '\{PORT\}', [string]$port) + "`r`n" }
            $head = "HTTP/1.1 " + $resp.status + " X`r`nContent-Type: application/json`r`n" + $extra + "Content-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n"
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
# גוגל מדומה **שעונה לכמה בקשות במקביל** (המרוץ, 0.8.14): בקשה עם hold מחכה בצד, והבאות נענות בינתיים
$serverC = {
    param($port, $responses, $logDir)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    $pending = New-Object System.Collections.ArrayList
    $k = 0
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    try {
        while (($k -lt $responses.Count -or $pending.Count -gt 0) -and [DateTime]::UtcNow -lt $deadline) {
            if ($k -lt $responses.Count -and $l.Pending()) {
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
                [IO.File]::WriteAllBytes((Join-Path $logDir ("req-$k.bin")), $ms.ToArray())
                $resp = $responses[$k]
                $hold = 0; if ($resp.hold) { $hold = [double]$resp.hold }
                [void]$pending.Add(@{ c = $c; ns = $ns; resp = $resp; until = [DateTime]::UtcNow.AddSeconds($hold) })
                $k++
            }
            foreach ($q in @($pending)) {
                $gone = $q.c.Client.Poll(0, [Net.Sockets.SelectMode]::SelectRead) -and $q.c.Client.Available -eq 0
                if ([DateTime]::UtcNow -ge $q.until -or $gone) {
                    if (-not $gone) {
                        $body = [Text.Encoding]::UTF8.GetBytes($q.resp.body)
                        $hb = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 " + $q.resp.status + " X`r`nContent-Type: application/json`r`nContent-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n")
                        try { $q.ns.Write($hb, 0, $hb.Length); $q.ns.Write($body, 0, $body.Length); $q.ns.Flush() } catch { }
                    }
                    $q.c.Close(); $pending.Remove($q)
                }
            }
            Start-Sleep -Milliseconds 15
        }
    } finally { $l.Stop() }
}
function StartServerC($responses) {
    $port = FreePort
    $dir = Join-Path $work ('srvc-' + $port); New-Item -ItemType Directory $dir | Out-Null
    $ps = [powershell]::Create(); [void]$ps.AddScript($serverC).AddArgument($port).AddArgument($responses).AddArgument($dir)
    $h = $ps.BeginInvoke(); Start-Sleep -Milliseconds 300
    (TY 'Ai').GetField('EndpointRoot', $ST).SetValue($null, "http://127.0.0.1:$port/v1beta/models/")
    return @{ Port = $port; Ps = $ps; Handle = $h; Dir = $dir }
}
# שורת הבקשה (POST /v1beta/models/<דגם>:generateContent...) והדגם שבה
function ReqLine($srv, $k) { $f = Join-Path $srv.Dir "req-$k.bin"; if (-not (Test-Path $f)) { return '' }; $s = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($f)); return $s.Substring(0, [Math]::Max(0, $s.IndexOf("`r`n"))) }
function ModelOf($line) { $m = [regex]::Match($line, 'models/([^:?]+):'); if ($m.Success) { return $m.Groups[1].Value }; return '' }function Req($srv, $k) { $f = Join-Path $srv.Dir "req-$k.bin"; if (-not (Test-Path $f)) { return '' }; $b = [IO.File]::ReadAllBytes($f); $s = [Text.Encoding]::UTF8.GetString($b); $i = $s.IndexOf("`r`n`r`n"); return $s.Substring($i + 4) }
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
[Windows.Forms.Application]::add_ThreadException({ param($s, $ev) [void]$script:crashes.Add($ev.Exception.ToString()); Write-Host ('    !! חריגה: ' + $ev.Exception.GetType().Name + ': ' + $ev.Exception.Message) -ForegroundColor Red; Write-Host (($ev.Exception.StackTrace -split "`n" | Select-Object -First 4) -join "`n") })
$script:winSince = $null
$guard = New-Object Windows.Forms.Timer; $guard.Interval = 500
$guard.Add_Tick({
    $w = (TY 'AiUi').GetMethod('Current', $ST).Invoke($null, @($mf, $chat))
    if (-not $w) { $script:winSince = $null; return }
    if (-not $script:winSince) { $script:winSince = Get-Date }
    elseif (((Get-Date) - $script:winSince).TotalSeconds -gt 20) { Write-Host "    (חלון נסגר בידי הבדיקה: $($w.Text))"; $w.Close(); $script:winSince = $null }
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

# ---------- 2א. תשובה ריקה כבר בתחילת הבקשה - עוד ניסיון לבד, בדגם הבא (0.8.15) ----------
$srv = StartServer @((Ok200 '{"candidates":[{"content":{"role":"model","parts":[]},"finishReason":"STOP"}]}'), (Reply (Txt 'הזזתי.')))
Say 'הכתוביות מאחרות בחצי שנייה'
$e1 = Req $srv 1; $em0 = ModelOf (ReqLine $srv 0); $em1 = ModelOf (ReqLine $srv 1)
Check 'תשובה ריקה בהתחלה: עוד ניסיון לבד, בדגם אחר - והתשובה מגיעה (עד 0.8.15: ״המודל לא החזיר תשובה״)' ($e1.Contains('התשובה שלך הגיעה ריקה') -and (LastModelText) -eq 'הזזתי.' -and $em0 -ne $em1) ("$em0 / $em1")
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

# ---------- 6ב. דגם שדוחה את כיבוי החשיבה ב-״invalid argument״ בלי לומר על מה - ניסיון בלעדיו ----------
$srv = StartServer @(
    @{ status = '400 Bad Request'; body = '{"error":{"code":400,"message":"Request contains an invalid argument.","status":"INVALID_ARGUMENT"}}' },
    (Reply (Txt 'עניתי בלי כיבוי החשיבה.')))
Say 'שאלה לדגם הקל'
$r6b = Req $srv 1
Check '״invalid argument״ - ניסיון נוסף בלי thinkingConfig, והתשובה מגיעה' ((LastModelText) -eq 'עניתי בלי כיבוי החשיבה.' -and -not $r6b.Contains('thinkingConfig')) ''

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

# ---------- 9. גוגל איטי: אחרי HedgeMs - אותה בקשה גם לדגם אחר, והראשון שעונה מנצח (0.8.14) ----------
# 8 סימן את כל הדגמים כ״מיצו את המכסה היומית״ - זה נשמר עד סוף הסשן
(TY 'Ai').GetMethod('ForgetExhausted', $ST).Invoke($null, @())
$hedge0 = $chT.GetField('HedgeMs', $ST).GetValue($null)
$chT.GetField('HedgeMs', $ST).SetValue($null, 1500)
$slowReply = Reply (Txt 'תשובה מאוחרת'); $slowReply.hold = 25
$srv = StartServerC @($slowReply, (Reply (Txt 'תשובה מהדגם השני.')))
$sw = [Diagnostics.Stopwatch]::StartNew()
Say 'שאלה כשגוגל איטי' 30
$took = $sw.Elapsed.TotalSeconds
$m0 = ModelOf (ReqLine $srv 0); $m1 = ModelOf (ReqLine $srv 1)
Check 'גוגל איטי: אחרי שנייה וחצי גם דגם אחר - והתשובה שלו מגיעה, לא אחרי 25 שניות' ((LastModelText) -eq 'תשובה מהדגם השני.' -and $took -lt 10) ("" + [Math]::Round($took, 1) + " שניות")
Check 'הבקשה המקבילה הלכה לדגם אחר' ($m0 -ne '' -and $m1 -ne '' -and $m0 -ne $m1) ("$m0 / $m1")
$chT.GetField('HedgeMs', $ST).SetValue($null, $hedge0)

# ---------- 10. הודעה קולית: קודם המילים (דגם קל), הן בבועה, ורק אז הבקשה - בטקסט (0.8.14) ----------
$pcm = New-Object byte[] (16000 * 2 * 3 / 2)
for ($q = 0; $q -lt $pcm.Length / 2; $q++) { $v = [int16]([Math]::Sin($q * 2 * [Math]::PI * 220 / 16000) * 9000); $pcm[2 * $q] = [byte]($v -band 0xFF); $pcm[2 * $q + 1] = [byte](($v -shr 8) -band 0xFF) }
$wav = (TY 'AiVoice').GetMethod('Wav', $ST).Invoke($null, @([byte[]]$pcm, [int]16000))
$sendVoice = $chT.GetMethod('SendVoice', $IN)
function VoiceItems { @($view.GetType().GetField('_items', $IN).GetValue($view) | Where-Object { $_.GetType().GetField('Voice').GetValue($_) }) }
function LastVoiceText { $v = VoiceItems; if ($v.Count -eq 0) { return '' }; return $v[$v.Count - 1].GetType().GetField('Text').GetValue($v[$v.Count - 1]) }
function LastItemText { $items = @($view.GetType().GetField('_items', $IN).GetValue($view)); $x = $items[$items.Count - 1]; return $x.GetType().GetField('Text').GetValue($x) }
$hearModels = (TY 'AiVoice').GetField('HearModels', $ST).GetValue($null)
$srv = StartServer @((Reply (Txt 'תוסיף כתובית בסוף')), (Reply (Txt 'הוספתי.')))
$sendVoice.Invoke($chat, @([byte[]]$wav, [double]1.5)); Pump 30
$h0 = Req $srv 0; $h1 = Req $srv 1
Check 'הודעה קולית: המילים שנשמעו - בבועה (לא ״הודעה קולית (2 שניות)״)' ((LastVoiceText) -eq 'תוסיף כתובית בסוף') (LastVoiceText)
Check 'התמלול - בדגם קל, עם ההקלטה' ((ModelOf (ReqLine $srv 0)) -eq $hearModels[0] -and $h0.Contains('inline_data') -and $h0.Contains('audio/wav')) (ModelOf (ReqLine $srv 0))
Check 'והבקשה לעוזר - המילים, בלי ההקלטה, והוא עונה' ($h1.Contains('תוסיף כתובית בסוף') -and -not $h1.Contains('inline_data') -and (LastModelText) -eq 'הוספתי.') ''
for ($q = 0; $q -lt 10; $q++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
$spoken = (TY 'AiVoice').GetField('LastSpoken', $ST).GetValue($null)
Check 'מי שדיבר - שומע תשובה (גם כשההקראה כבויה)' ($spoken -eq 'הוספתי.') $spoken

# ---------- 11. התמלול נכשל בכל הדגמים הקלים - ההקלטה עצמה נשלחת לעוזר, כמו עד 0.8.14 ----------
$denied = @{ status = '403 Forbidden'; body = '{"error":{"code":403,"message":"denied","status":"PERMISSION_DENIED"}}' }
$seq = @(); foreach ($x in $hearModels) { $seq += $denied }; $seq += (Reply (Txt 'שמעתי את ההקלטה.'))
$srv = StartServer $seq
$sendVoice.Invoke($chat, @([byte[]]$wav, [double]1.5)); Pump 30
$hf = Req $srv $hearModels.Count
Check 'תמלול נכשל - ההקלטה עצמה הולכת לעוזר, והבועה אומרת ״הודעה קולית״' ($hf.Contains('inline_data') -and (LastVoiceText) -like '*(*' -and (LastModelText) -eq 'שמעתי את ההקלטה.') (LastVoiceText)

# ---------- 12. בהקלטה אין מילים - לא נשלח כלום לעוזר, ונאמר למה ----------
$srv = StartServer @((Ok200 '{"candidates":[{"content":{"role":"model","parts":[]},"finishReason":"STOP"}]}'))
$n0 = (VoiceItems).Count
$sendVoice.Invoke($chat, @([byte[]]$wav, [double]1.5)); Pump 15
Check 'אין דיבור: הבועה יורדת, ״לא שמעתי מילים״, והעוזר פנוי' ((VoiceItems).Count -eq $n0 -and (LastItemText) -like '*לא שמעתי מילים*' -and -not $fBusy.GetValue($chat) -and (Req $srv 1) -eq '') (LastItemText)

# ---------- 13. עצירה באמצע התמלול ----------
$srv = StartServer @(@{ status = '200 OK'; body = '{}'; hold = 20 })
$sendVoice.Invoke($chat, @([byte[]]$wav, [double]1.5))
for ($k = 0; $k -lt 15; $k++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 }
$sw = [Diagnostics.Stopwatch]::StartNew()
$chT.GetMethod('StopRequest', $IN).Invoke($chat, @())
Pump 10
Check 'עצירה באמצע התמלול: פנוי תוך שניות, והבועה לא נשארת על ״…״' ((-not $fBusy.GetValue($chat)) -and $sw.Elapsed.TotalSeconds -lt 5 -and (LastVoiceText) -ne '…') ("" + [Math]::Round($sw.Elapsed.TotalSeconds, 1) + " שניות, " + (LastVoiceText))
# ---------- 14. עבודה שהעוזר הפעיל הסתיימה - שורה בשיחה, והעוזר יודע בבקשה הבאה (0.8.15) ----------
(TY 'AiUi').GetField('DeferredDepth', $ST).SetValue($null, 1)
$chT.GetMethod('OnJobEnded', $IN).Invoke($chat, @($true, $false, [string]'נשמר: סרט - וואטסאפ.mp4 · 4.1 MB', [string]'D:\x\סרט - וואטסאפ.mp4'))
(TY 'AiUi').GetField('DeferredDepth', $ST).SetValue($null, 0)
$endLine = LastItemText
$srv = StartServer @((Reply (Txt 'הקובץ נשמר ליד הסרט.')))
Say 'איפה הקובץ?'
Check 'סוף עבודה שהעוזר הפעיל: ״נשמר: ...״ בשיחה, ובבקשה הבאה העוזר יודע' ($endLine -like '*נשמר*וואטסאפ*' -and (Req $srv 0).Contains('העבודה שהפעלת קודם הסתיימה')) $endLine
# ---------- 15. צפייה בסרט: העלאה פעם אחת, שאלה, זמנים של הסרט המלא, ושאלה נוספת בלי להעלות שוב (0.8.17) ----------
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$wmedia = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $wmedia)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build\make-testmedia.ps1') | Out-Null }
[void]$mfT.GetMethod('OpenMedia', $IN).Invoke($mf, @([string]$wmedia))
for ($k = 0; $k -lt 20; $k++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
$fileJson = '{"file":{"name":"files/abc123","uri":"https://generativelanguage.googleapis.com/v1beta/files/abc123","state":"ACTIVE","mimeType":"video/mp4"}}'
$srv = StartServer @(
    (Reply (Call 'watch_video' '{"question":"על מה הסרט?"}' $null)),
    @{ status = '200 OK'; body = '{}'; headers = 'X-Goog-Upload-URL: http://127.0.0.1:{PORT}/upload-session' },
    (Ok200 $fileJson),
    (Reply (Txt 'יש בו מלך שיושב על כס, ב-[[0:43]].')),
    (Reply (Txt 'בסרט יש מלך על כס, ב-0:43.')),
    (Reply (Call 'watch_video' '{"question":"מי עוד מופיע?"}' $null)),
    (Reply (Txt 'אין עוד דמויות.')),
    (Reply (Txt 'אין עוד דמויות.')))
(TY 'Ai').GetField('UploadRoot', $ST).SetValue($null, "http://127.0.0.1:$($srv.Port)/upload/v1beta/files")
(TY 'Ai').GetField('FilesRoot', $ST).SetValue($null, "http://127.0.0.1:$($srv.Port)/v1beta/")
Say 'על מה הסרט?' 60
$w3 = Req $srv 3; $w4 = Req $srv 4
Check 'צפייה: הקובץ עלה (שתי בקשות), והשאלה נשלחה עם הקובץ וברזולוציה הנמוכה' ((ReqLine $srv 1) -like '*upload/v1beta/files*' -and (ReqLine $srv 2) -like '*upload-session*' -and $w3.Contains('file_data') -and $w3.Contains('files/abc123') -and $w3.Contains('MEDIA_RESOLUTION_LOW')) (ReqLine $srv 3)
Check 'התשובה חוזרת לעוזר עם הזמן של הסרט (״0:43״, בלי [[ ]])' ($w4.Contains('0:43') -and -not $w4.Contains('[[0:43]]') -and (LastModelText) -eq 'בסרט יש מלך על כס, ב-0:43.') ''
Say 'מי עוד מופיע?' 60
$w5 = Req $srv 5
Check 'שאלה נוספת על אותו סרט - בלי להעלות שוב, ומה שכבר הובן נמצא במצב' ((ModelOf (ReqLine $srv 6)) -ne '' -and (ReqLine $srv 6) -like '*generateContent*' -and (Req $srv 6).Contains('files/abc123') -and $w5.Contains('מה כבר ידוע לך על תוכן הסרט') -and (LastModelText) -eq 'אין עוד דמויות.') (ReqLine $srv 6)

# ---------- 15א. סרט ארוך - בחלקים, עד הסוף (0.8.18). בנימין: ״מחפש רק ב-30 השניות הראשונות״ ----------
$f1 = '{"file":{"name":"files/p1","uri":"https://generativelanguage.googleapis.com/v1beta/files/p1","state":"ACTIVE","mimeType":"video/mp4"}}'
$f2 = '{"file":{"name":"files/p2","uri":"https://generativelanguage.googleapis.com/v1beta/files/p2","state":"ACTIVE","mimeType":"video/mp4"}}'
$srv = StartServer @(
    @{ status = '200 OK'; body = '{}'; headers = 'X-Goog-Upload-URL: http://127.0.0.1:{PORT}/upload-session' }, (Ok200 $f1), (Reply (Txt 'אין בחלק הזה')),
    @{ status = '200 OK'; body = '{}'; headers = 'X-Goog-Upload-URL: http://127.0.0.1:{PORT}/upload-session' }, (Ok200 $f2), (Reply (Txt 'הוא נכנס לחדר ב-[[0:02]].')))
(TY 'Ai').GetField('UploadRoot', $ST).SetValue($null, "http://127.0.0.1:$($srv.Port)/upload/v1beta/files")
(TY 'Ai').GetField('FilesRoot', $ST).SetValue($null, "http://127.0.0.1:$($srv.Port)/v1beta/")
$parts = [Collections.Generic.List[long[]]]::new(); $parts.Add([long[]]@(0, 4000)); $parts.Add([long[]]@(4000, 8000))
$stl = New-Object Collections.ArrayList
$apa = New-Object object[] 7; $apa[0] = 'מתי הוא נכנס לחדר?'; $apa[1] = [string]$wmedia; $apa[2] = $parts; $apa[3] = $true; $apa[4] = [int]360; $apa[5] = [Action[string]] { param($s) [void]$stl.Add($s) }; $apa[6] = $null
$pans = (TY 'AiWatch').GetMethod('AskParts', $ST).Invoke($null, $apa)
$q1 = Req $srv 2; $q2 = Req $srv 5
Check 'סרט ארוך: כל חלק נשאל לבד (״חלק 1 מתוך 2״), עם הקובץ שלו' ($q1.Contains('חלק 1 מתוך 2') -and $q1.Contains('files/p1') -and $q2.Contains('חלק 2 מתוך 2') -and $q2.Contains('files/p2')) ''
Check 'והתשובות מחוברות לפי הסדר, עם הזמן בסרט המלא (0:02 בחלק השני = 0:06)' (('' + $pans).Contains('[0:00-0:04] אין בחלק הזה') -and ('' + $pans).Contains('[0:04-0:08]') -and ('' + $pans).Contains('ב-0:06') -and $stl.Count -eq 2) ('' + $pans).Replace("`n", ' / ')
# ---------- 16. טקסט מהתמונה: פריים ברזולוציה מלאה, והטקסט חוזר לעוזר ----------
$srv = StartServer @(
    (Reply (Call 'read_text_on_screen' '{"seconds":2}' $null)),
    (Reply (Txt 'כתובית: היום נלמד על כתוביות')),
    (Reply (Txt 'כתוב: היום נלמד על כתוביות.')))
Say 'מה כתוב על המסך?' 40
$o1 = Req $srv 1; $o2 = Req $srv 2
Check 'קריאת טקסט: תמונה ברזולוציה מלאה נשלחת, והטקסט חוזר לעוזר' ($o1.Contains('inline_data') -and $o1.Contains('image/jpeg') -and $o2.Contains('text_on_screen') -and $o2.Contains('היום נלמד על כתוביות')) ''
# ---------- 17. כתוביות צרובות ← כתוביות: חיפוש בתמונה (בלי רשת), רצועות לגוגל, וכתוביות עם זמנים (0.8.17) ----------
$synth = Join-Path $env:TEMP 'ss-burn-synth.mp4'
if (-not (Test-Path $synth)) {
    $ffx = (TY 'Ff').GetProperty('Exe', $ST).GetValue($null, $null)
    $vfx = "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='HELLO WORLD':fontsize=40:fontcolor=white:borderw=3:bordercolor=black:x=(w-tw)/2:y=h-70:enable='between(t,1,3)',drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='SECOND LINE HERE':fontsize=40:fontcolor=white:borderw=3:bordercolor=black:x=(w-tw)/2:y=h-70:enable='between(t,4,6.5)'"
    & $ffx -hide_banner -v error -y -f lavfi -i "testsrc2=size=640x360:rate=25:duration=8" -vf $vfx -c:v libx264 -pix_fmt yuv420p $synth 2>&1 | Out-Null
}
[void]$mfT.GetMethod('OpenMedia', $IN).Invoke($mf, @([string]$synth))
for ($k = 0; $k -lt 20; $k++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
$srv = StartServer @(
    (Reply (Call 'extract_burned_subtitles' '{}' $null)),
    (Reply (Call 'extract_burned_subtitles' '{"mode":"replace"}' $null)),
    (Reply (Txt '[{\"n\":1,\"text\":\"HELLO WORLD\"},{\"n\":2,\"text\":\"SECOND LINE HERE\"}]')),
    (Reply (Txt 'הוצאתי שתי כתוביות מהתמונה.')))
Say 'תוציא את הכתוביות הצרובות מהסרט' 60
$b1 = Req $srv 1; $b2 = Req $srv 2
$bc = Cues
Check 'כתוביות צרובות כשיש כבר כתוביות: העוזר מקבל ״לשאול אם להחליף או להוסיף״ לפני שמשהו נמחק' ($b1.Contains('mode=replace')) ''
Check 'הרצועות נשלחות לגוגל כתמונה, עם ההוראה' ($b2.Contains('inline_data') -and $b2.Contains('image/jpeg') -and $b2.Contains('רצועות ממוספרות')) ''
$s0 = if ($bc.Count -gt 0) { (TY 'Cue').GetField('Start').GetValue($bc[0]) } else { -1 }; $s1 = if ($bc.Count -gt 1) { (TY 'Cue').GetField('Start').GetValue($bc[1]) } else { -1 }
Check 'שתי הכתוביות במסמך, עם הטקסט ובזמן (בתוך חצי שנייה)' ($bc.Count -eq 2 -and (CueText 0) -eq 'HELLO WORLD' -and (CueText 1) -eq 'SECOND LINE HERE' -and [Math]::Abs($s0 - 1000) -le 500 -and [Math]::Abs($s1 - 4000) -le 500) ("" + $bc.Count + ": " + $s0 + ", " + $s1)
Check 'בשיחה: ״נוספו 2 כתוביות מהתמונה״' (@($view.GetType().GetField('_items', $IN).GetValue($view) | Where-Object { $_.GetType().GetField('Text').GetValue($_) -like '*נוספו 2 כתוביות מהתמונה*' }).Count -ge 1) ''
Check 'בלי חריגות בחלון השיחה' ($script:crashes.Count -eq 0) ("" + $script:crashes.Count)
$guard.Stop()
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
# שרת מדומה שלא קיבל את כל הבקשות שלו (בדיקה שנכשלה) תקוע ב-AcceptTcpClient ומחזיק את התהליך חי - test-all חיכה לו עד הזמן הקצוב
[Environment]::Exit($(if ($fail -gt 0) { 1 } else { 0 }))
