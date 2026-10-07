# שירות התמלול (ISttProvider) מול שרת Groq מדומה - בלי מפתח, בלי רשת.
#
# **למה מדומה:** אין לנו מפתח של Groq, ולא פותחים חשבון בשם המשתמש. וגם
# אם היה: את המצבים החשובים באמת אי אפשר לייצר מול השרת האמיתי בלי לשרוף
# מכסה - מפתח שגוי, מכסה שעתית שנגמרה, מכסה יומית, שרת שנפל. השרת כאן
# הוא TcpListener (‏HttpListener דורש הרשאות), והוא עונה בתשובות שמנוסחות
# כמו של Groq, ושומר כל בקשה כדי לבדוק מה נשלח.
#
# **מה לא נבדק כאן:** השרת האמיתי. אחרי שיש מפתח - להריץ תמלול אמיתי אחד
# ולתעד ב-CLAUDE.md.
# צפוי: 118 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($null -ne $v) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }
$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$work = [IO.Path]::GetFullPath((Join-Path $env:TEMP ('ss-stt-' + [guid]::NewGuid().ToString('N').Substring(0, 6))))
New-Item -ItemType Directory $work | Out-Null

# ---------------- השרת המדומה ----------------
$server = {
    param($port, $responses, $logDir)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    try {
        for ($k = 0; $k -lt $responses.Count; $k++) {
            $resp = $responses[$k]
            $c = $l.AcceptTcpClient()
            $ns = $c.GetStream()
            $ns.ReadTimeout = 15000
            $ms = New-Object IO.MemoryStream
            $buf = New-Object byte[] 65536
            $headEnd = -1; $clen = 0
            while ($true) {
                $n = $ns.Read($buf, 0, $buf.Length)
                if ($n -le 0) { break }
                $ms.Write($buf, 0, $n)
                $txt = [Text.Encoding]::ASCII.GetString($ms.ToArray())
                if ($headEnd -lt 0) {
                    $headEnd = $txt.IndexOf("`r`n`r`n")
                    if ($headEnd -ge 0) {
                        $m = [regex]::Match($txt.Substring(0, $headEnd), '(?im)^content-length:\s*(\d+)')
                        if ($m.Success) { $clen = [int]$m.Groups[1].Value }
                    }
                }
                if ($headEnd -ge 0 -and $ms.Length -ge $headEnd + 4 + $clen) { break }
            }
            [IO.File]::WriteAllBytes((Join-Path $logDir ("req-$k.bin")), $ms.ToArray())
            $body = [Text.Encoding]::UTF8.GetBytes($resp.body)
            $head = "HTTP/1.1 " + $resp.status + " X`r`nContent-Type: application/json`r`nContent-Length: " + $body.Length + "`r`n"
            if ($resp.retry) { $head += "retry-after: " + $resp.retry + "`r`n" }
            $head += "Connection: close`r`n`r`n"
            $hb = [Text.Encoding]::ASCII.GetBytes($head)
            $ns.Write($hb, 0, $hb.Length); $ns.Write($body, 0, $body.Length); $ns.Flush()
            $c.Close()
        }
    } finally { $l.Stop() }
}
function FreePort { $t = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), 0; $t.Start(); $p = $t.LocalEndpoint.Port; $t.Stop(); return $p }
function StartServer($responses) {
    $port = FreePort
    $dir = Join-Path $work ('srv-' + $port); New-Item -ItemType Directory $dir | Out-Null
    $ps = [powershell]::Create()
    [void]$ps.AddScript($server).AddArgument($port).AddArgument($responses).AddArgument($dir)
    $h = $ps.BeginInvoke()
    Start-Sleep -Milliseconds 400
    return @{ Port = $port; Ps = $ps; Handle = $h; Dir = $dir }
}
function StopServer($s) {
    if (-not $s.Handle.AsyncWaitHandle.WaitOne(20000)) { $s.Ps.Stop() }
    try { [void]$s.Ps.EndInvoke($s.Handle) } catch { Write-Host ("   server: " + $_.Exception.Message) }
    foreach ($e in $s.Ps.Streams.Error) { Write-Host ("   server error: " + $e) }
    $s.Ps.Dispose()
}
function Resp($status, $body, $retry) { return @{ status = $status; body = $body; retry = $retry } }
function NewProvider($port) {
    $p = [Activator]::CreateInstance((T 'OpenAiStt'))
    $p.BaseUrl = "http://127.0.0.1:$port/openai/v1"
    $p.KeyOverride = 'gsk_test_123'
    return $p
}
function Chunk($p, [byte[]]$audio, $ctx) {
    $a = New-Object object[] 4; $a[0] = $audio; $a[1] = 'audio/mp3'; $a[2] = [string]$ctx; $a[3] = $null
    $lines = $p.GetType().GetMethod('TranscribeChunk').Invoke($p, $a)
    return @{ Lines = $lines; Error = $a[3] }
}
$audio = New-Object byte[] 20000; (New-Object Random 1).NextBytes($audio)

# ================= 1. בדיקת מפתח =================
Write-Host 'בדיקת מפתח'
$s = StartServer @((Resp 200 '{"object":"list","data":[]}' $null), (Resp 401 '{"error":{"message":"Invalid API Key","type":"invalid_request_error"}}' $null))
$p = NewProvider $s.Port
$a = New-Object object[] 2; $a[0] = 'gsk_test_123'
$ok1 = $p.GetType().GetMethod('CheckKey').Invoke($p, $a)
$a2 = New-Object object[] 2; $a2[0] = 'gsk_wrong'
$ok2 = $p.GetType().GetMethod('CheckKey').Invoke($p, $a2)
StopServer $s
$req0 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-0.bin')))
Check 'מפתח תקין מתקבל' ($ok1 -eq $true) $a[1]
Check 'הבדיקה היא GET לרשימת הדגמים (לא עולה מכסה)' ($req0.StartsWith('GET /openai/v1/models')) ($req0.Split("`n")[0])
Check 'המפתח נשלח ככותרת Bearer' ($req0 -match '(?im)^Authorization: Bearer gsk_test_123') ''
Check 'מפתח שגוי: הודעה על המפתח' (($ok2 -eq $false) -and $a2[1] -match 'המפתח') $a2[1]

# ================= 2. תמלול קטע =================
Write-Host 'תמלול קטע'
$long = 'זה משפט ארוך מאוד שהמודל החזיר כקטע אחד, בלי לחלק אותו, והוא ממשיך וממשיך. ואז עוד חלק שני של אותו משפט ארוך, שלא נכנס בשום אופן בכתובית אחת על המסך.'
$ok = @"
{"task":"transcribe","language":"hebrew","duration":30.0,"text":"...","segments":[
 {"id":0,"start":0.0,"end":3.2,"text":" שלום לכולם","avg_logprob":-0.2,"compression_ratio":1.1,"no_speech_prob":0.01},
 {"id":1,"start":5.0,"end":7.0,"text":" תודה רבה.","avg_logprob":-1.3,"compression_ratio":0.9,"no_speech_prob":0.92},
 {"id":2,"start":8.0,"end":24.0,"text":" $long","avg_logprob":-0.3,"compression_ratio":1.4,"no_speech_prob":0.02},
 {"id":3,"start":24.5,"end":28.0,"text":" כן כן כן כן כן כן כן כן כן כן כן כן","avg_logprob":-0.4,"compression_ratio":3.1,"no_speech_prob":0.05},
 {"id":4,"start":28.2,"end":29.5,"text":" תודה רבה.","avg_logprob":-0.25,"compression_ratio":0.9,"no_speech_prob":0.03},
 {"id":5,"start":30.0,"end":47.0,"text":" תודה רבה.","avg_logprob":-1.2,"compression_ratio":0.9,"no_speech_prob":0.17},
 {"id":6,"start":48.0,"end":58.0,"text":" אה","avg_logprob":-0.5,"compression_ratio":0.9,"no_speech_prob":0.1}
]}
"@
$s = StartServer @((Resp 200 $ok $null))
$p = NewProvider $s.Port
$ctx = 'שיעור בגמרא, מסכת ברכות, עם הרבה מונחים בארמית ושמות של אמוראים ותנאים שהמודל לא מכיר בדרך כלל ועוד ועוד'
$r = Chunk $p $audio $ctx
StopServer $s
$lines = @($r.Lines)
$bytes = [IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-0.bin'))
$req = [Text.Encoding]::UTF8.GetString($bytes)
Check 'נקרא בלי שגיאה' ($r.Lines -ne $null) $r.Error
Check 'POST לנקודת התמלול' ($req.StartsWith('POST /openai/v1/audio/transcriptions')) ''
Check 'multipart עם boundary' ($req -match '(?im)^Content-Type: multipart/form-data; boundary=----SubStudio') ''
Check 'הדגם הראשון: whisper-large-v3' ($req -match 'name="model"\r\n\r\nwhisper-large-v3\r\n') ''
Check 'verbose_json + זמנים לפי קטע' (($req -match 'name="response_format"\r\n\r\nverbose_json') -and ($req -match 'name="timestamp_granularities\[\]"\r\n\r\nsegment')) ''
Check 'וגם זמן לכל מילה (0.8.3)' ($req -match 'name="timestamp_granularities\[\]"\r\n\r\nword') ''
$pm = [regex]::Match($req, 'name="prompt"\r\n\r\n([^\r]*)\r\n')
Check 'הרקע נשלח כרמז, חתוך ל-100 תווים' ($pm.Success -and $pm.Groups[1].Value.Length -eq 100 -and $ctx.StartsWith($pm.Groups[1].Value)) ("len=" + $pm.Groups[1].Value.Length)
Check 'הקול עצמו בגוף, בייט-בייט' ($bytes.Length -gt $audio.Length -and $req -match 'filename="chunk\.mp3"\r\nContent-Type: audio/mpeg') ("body=" + $bytes.Length)
Check 'שורה ראשונה: טקסט בלי רווח מוביל, בזמנים שלה' ($lines.Count -gt 0 -and $lines[0].Text -eq 'שלום לכולם' -and $lines[0].Start -eq 0 -and $lines[0].End -eq 3.2) ''
Check '״תודה רבה״ בשקט (Whisper לא בטוח שהיה דיבור) - נזרק' (-not ($lines | Where-Object { $_.Start -eq 5.0 })) ''
Check 'לולאת חזרה (compression_ratio 3.1) - נזרקת' (-not ($lines | Where-Object { $_.Text -like 'כן כן*' })) ''
Check '״תודה רבה״ שנאמר באמת - נשאר' (@($lines | Where-Object { $_.Start -eq 28.2 }).Count -eq 1) ''
# נמדד בסבב על דרשה אמיתית: ״תודה רבה״ אחת על 30 שניות, no_speech 0.17 - וחצי מהתוכן נעלם בלי סימן
Check '״תודה רבה״ על 17 שניות - נזרק גם כש-no_speech נמוך' (-not ($lines | Where-Object { $_.Start -eq 30.0 })) ''
Check 'שתי אותיות על 10 שניות - נזרק' (-not ($lines | Where-Object { $_.Start -eq 48.0 })) ''
$split = @($lines | Where-Object { $_.Start -ge 8.0 -and $_.End -le 24.0 })
$maxLen = 0; $maxDur = 0; foreach ($x in $split) { if ($x.Text.Length -gt $maxLen) { $maxLen = $x.Text.Length }; if ($x.End - $x.Start -gt $maxDur) { $maxDur = $x.End - $x.Start } }
$joined = ($split | ForEach-Object { $_.Text }) -join ' '
Check 'קטע של 16 שניות פוצל לכתוביות קריאות' ($split.Count -ge 3 -and $maxLen -le 90 -and $maxDur -le 7.5) ("parts=" + $split.Count + " maxLen=$maxLen maxDur=" + [Math]::Round($maxDur, 2))
Check 'בלי לאבד מילה בפיצול' (($joined -replace '\s+', ' ') -eq ($long -replace '\s+', ' ')) ''
Check 'הזמנים של הפיצול רציפים ומכסים את כל הקטע' ($split[0].Start -eq 8.0 -and $split[$split.Count - 1].End -eq 24.0) ''

# ================= 3. מכסות ותקלות =================
Write-Host 'מכסות ותקלות'
$hour = '{"error":{"message":"Rate limit reached for model `whisper-large-v3` in organization `org_x` service tier `on_demand` on audio seconds per hour (ASH): Limit 7200, Used 7190, Requested 180. Please try again in 20m.","type":"audio_seconds","code":"rate_limit_exceeded"}}'
$day = '{"error":{"message":"Rate limit reached for model `whisper-large-v3-turbo` in organization `org_x` on audio seconds per day (ASD): Limit 28800, Used 28790.","code":"rate_limit_exceeded"}}'
$minute = '{"error":{"message":"Rate limit reached for model `whisper-large-v3` on requests per minute (RPM): Limit 20, Used 20.","code":"rate_limit_exceeded"}}'
$s = StartServer @((Resp 429 $hour 1200), (Resp 429 $day 50000))
$p = NewProvider $s.Port
$r1 = Chunk $p $audio ''
$retry1 = $p.LastRetrySec; $model1 = $p.CurrentModel; $daily1 = $p.LastQuotaIsDaily
$r2 = Chunk $p $audio ''
StopServer $s
$req1 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-1.bin')))
Check 'מכסה שעתית: נכשל, אבל כדאי לנסות מיד' ($r1.Lines -eq $null -and $retry1 -eq 1 -and -not $daily1) ("retry=$retry1")
Check 'ועובר לדגם השני (מכסה נפרדת)' ($model1 -eq 'whisper-large-v3-turbo') $model1
Check 'הבקשה הבאה באמת נשלחה בדגם השני' ($req1 -match 'name="model"\r\n\r\nwhisper-large-v3-turbo\r\n') ''
Check 'גם השני נגמר: מכסה ״נגמרה לעכשיו״, בלי ניסיון נוסף' ($r2.Lines -eq $null -and $p.LastQuotaIsDaily -and $p.LastRetrySec -eq 0) ("daily=" + $p.LastQuotaIsDaily + " retry=" + $p.LastRetrySec)
Check 'ההודעה מדברת על מכסה, לא על ״שגיאה 429״' ($r2.Error -match 'המכסה') $r2.Error

$s = StartServer @((Resp 429 $minute 7), (Resp 500 '{"error":{"message":"internal"}}' $null), (Resp 400 '{"error":{"message":"file must be one of the following types"}}' $null))
$p = NewProvider $s.Port
$r3 = Chunk $p $audio ''; $retry3 = $p.LastRetrySec; $model3 = $p.CurrentModel
$r4 = Chunk $p $audio ''; $retry4 = $p.LastRetrySec
$r5 = Chunk $p $audio ''; $retry5 = $p.LastRetrySec
StopServer $s
Check 'מכסה לדקה: מחכים כמה שה-retry-after אומר, בלי להחליף דגם' ($retry3 -eq 7 -and $model3 -eq 'whisper-large-v3') ("retry=$retry3 model=$model3")
Check 'שרת שנפל: לנסות שוב בעוד כמה שניות, והודעה אנושית' ($retry4 -gt 0 -and $r4.Error -match 'לא זמין') $r4.Error
Check 'שגיאה אחרת: ההודעה מהשרת עוברת הלאה, בלי ניסיון חוזר' ($retry5 -eq 0 -and $r5.Error -match 'file must be') $r5.Error

$p = NewProvider (FreePort)
$r6 = Chunk $p $audio ''
Check 'אין שרת בכלל: ״אין חיבור לאינטרנט, או שהשירות חסום״' ($r6.Lines -eq $null -and $r6.Error -match 'אין חיבור') $r6.Error
$pNoKey = [Activator]::CreateInstance((T 'OpenAiStt')); $pNoKey.KeyOverride = ''
Check 'בלי מפתח: לא יוצאת בקשה בכלל' (-not $pNoKey.HasKey) ''

# ================= 4. תמלול קובץ שלם דרך Transcribe =================
Write-Host 'תמלול קובץ שלם (שני קטעים, חפיפה)'
(T 'Runtime').GetMethod('Prepare', $SF).Invoke($null, @())
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $media)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }
# קטע ראשון 0-25, שני 20-45 (צעד 20, חפיפה 5). ״בגבול״ נאמר ב-21 ונלכד בשניהם.
$c0 = '{"segments":[{"start":1.0,"end":4.0,"text":"אחת","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01},{"start":21.0,"end":24.0,"text":"בגבול","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
$c1 = '{"segments":[{"start":1.0,"end":4.0,"text":"בגבול","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01},{"start":7.0,"end":9.5,"text":"שתיים","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
# הצליל בקובץ הבדיקה רצוף, כך שבכל קטע יש ״קול בלי כתוביות״ (Recover): בקטע הראשון
# בין 4 ל-21, בשני מ-29.5 עד הסוף. התיקון של הראשון מוצא שורה שהמודל דילג עליה.
# החור הוא 17 שניות של קול; תיקון שמכסה 6 מהן (30% לפחות) סוגר אותו
$fix0 = '{"segments":[{"start":5.0,"end":11.0,"text":"באמצע","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
$none = '{"segments":[]}'
# בקטע השני התיקון חוזר ריק, ואז עוד ניסיון בחלון מוזז (Groq), גם הוא ריק
$s = StartServer @((Resp 200 $c0 $null), (Resp 200 $fix0 $null), (Resp 200 $c1 $null), (Resp 200 $none $null), (Resp 200 $none $null))
$p = NewProvider $s.Port
$p.Chunk = 25
$trT = T 'Transcribe'
$run = $null
foreach ($m in $trT.GetMethods($SF)) { if ($m.Name -eq 'Run' -and $m.GetParameters().Count -eq 6) { $run = $m } }
$sw = [Diagnostics.Stopwatch]::StartNew()
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]40000) ([string]'') $null $null))
StopServer $s
$texts = @($res.Cues | ForEach-Object { $_.Text + '@' + $_.Start })
Check 'ארבע כתוביות: הכפילות בחפיפה נזרקה, והשורה מהחור נכנסה' ($res.Cues.Count -eq 4) ($texts -join ', ')
Check 'הזמנים מוזזים לפי תחילת הקטע, והתיקון לפי תחילת החור (3+5=8)' (($texts -join ',') -eq 'אחת@1000,באמצע@8000,בגבול@21000,שתיים@27000') ($texts -join ',')
Check 'שלוש בקשות תיקון: אחת בקטע הראשון (התמלא), ושתיים בשני (חלון מוזז)' ($res.Recovered -eq 3) ("recovered=" + $res.Recovered)
Check 'שם השירות בתוצאה' ($res.ProviderName -eq 'Groq') $res.ProviderName
$req1 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-1.bin')))
$req0 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-0.bin')))
Check 'הקטע עצמו בדגם הקבוע, והשליחה החוזרת בדגם השני (Whisper לא יציב)' (($req0 -match 'name="model"\r\n\r\nwhisper-large-v3\r\n') -and ($req1 -match 'name="model"\r\n\r\nwhisper-large-v3-turbo\r\n')) ''
Check 'ואחריה חוזרים לדגם הקבוע' ($p.CurrentModel -eq 'whisper-large-v3') $p.CurrentModel
Check 'בלי שגיאה' ($res.Error -eq $null -and $res.Failed -eq 0) ("error=" + $res.Error)
# החור שבסוף הקטע השני נשלח שוב וחזר ריק: עד 0.8.1 הוא נבלע. עכשיו הוא חור שמדווח
Check 'חור שגם השליחה החוזרת לא מילאה - מדווח, לא נבלע' ($res.Gaps.Count -eq 1 -and $res.Gaps[0] -match '29') ($res.Gaps -join ', ')

# ================= 4. ב. רק הקטע המסומן (0.8.6) =================
Write-Host 'רק קטע מהסרט'
# הקטע 10-35 בקובץ של 40 שניות: קטע אחד (25 שניות), והזמנים מתחילת הקטע. שורה ״אחרי״ הקטע - נזרקת
$cr = '{"segments":[{"start":1.0,"end":4.0,"text":"בקטע","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01},{"start":22.0,"end":24.5,"text":"סוף הקטע","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01},{"start":26.0,"end":28.0,"text":"מחוץ לקטע","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
$s = StartServer @((Resp 200 $cr $null), (Resp 200 $none $null), (Resp 200 $none $null))
$p = NewProvider $s.Port
$p.Chunk = 25
$run8 = $null
foreach ($m in $trT.GetMethods($SF)) { if ($m.Name -eq 'Run' -and $m.GetParameters().Count -eq 8) { $run8 = $m } }
$res = $run8.Invoke($null, (Pack $p ([string]$media) ([long]40000) ([string]'') $null $null ([long]10000) ([long]35000)))
StopServer $s
$texts = @($res.Cues | ForEach-Object { $_.Text + '@' + $_.Start })
Check 'קטע: הזמנים מתחילת הקטע (10+1), ובלי מה שמחוץ לו' (($texts -join ',') -eq 'בקטע@11000,סוף הקטע@32000') ($texts -join ', ')
Check 'קטע: נשלח קטע אחד בלבד (ושני תיקונים לחור שבתוכו)' ($res.Chunks -eq 1 -and $res.Recovered -eq 2) ("chunks=" + $res.Chunks + " recovered=" + $res.Recovered)

# ================= 4א. קטע שחזר ריק כולו, ויש בו קול =================
Write-Host 'קטע שחזר ריק, ויש בו קול'
# נמדד בסבב: דיאלוג ברור של 17 שניות חזר מ-Whisper כ״תודה רבה״ אחת (שנזרקת) - כלומר ריק
$phantom = '{"segments":[{"start":0.0,"end":17.0,"text":" תודה רבה.","avg_logprob":-1.2,"compression_ratio":0.9,"no_speech_prob":0.17}]}'
$good = '{"segments":[{"start":1.0,"end":7.0,"text":"דיאלוג אמיתי","avg_logprob":-0.3,"compression_ratio":1,"no_speech_prob":0.1}]}'
$s = StartServer @((Resp 200 $phantom $null), (Resp 200 $good $null))
$p = NewProvider $s.Port
$p.Chunk = 25
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]17000) ([string]'') $null $null))
StopServer $s
Check 'נשלח שוב, ומה שחזר נכנס' ($res.Recovered -eq 1 -and $res.Cues.Count -eq 1 -and $res.Cues[0].Text -eq 'דיאלוג אמיתי') ("recovered=" + $res.Recovered + " cues=" + $res.Cues.Count)
$s = StartServer @((Resp 200 $phantom $null), (Resp 200 $none $null), (Resp 200 $none $null))
$p = NewProvider $s.Port
$p.Chunk = 25
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]17000) ([string]'') $null $null))
StopServer $s
Check 'גם שני הניסיונות ריקים: לא ״לא זוהה דיבור״ אלא ״לא החזיר טקסט, למרות שיש קול״' ($res.Cues.Count -eq 0 -and $res.Error -match 'לא החזיר טקסט' -and $res.Gaps.Count -eq 1 -and $res.Recovered -eq 2) ($res.Error + " recovered=" + $res.Recovered)

# ״ובגירים״ בשנייה 30 של חור של 30 שניות סגרה אותו עד 0.8.1 (נמדד). מילה בקצה איננה מילוי
$edge = '{"segments":[{"start":29.0,"end":30.0,"text":"ובגירים","avg_logprob":-0.3,"compression_ratio":1,"no_speech_prob":0.1}]}'
$shifted = '{"segments":[{"start":0.5,"end":6.5,"text":"הדרשה עצמה","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.05},{"start":6.5,"end":12.5,"text":"והמשך הדרשה","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.05}]}'
$s = StartServer @((Resp 200 $phantom $null), (Resp 200 $edge $null), (Resp 200 $shifted $null))
$p = NewProvider $s.Port
$p.Chunk = 25
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]17000) ([string]'') $null $null))
StopServer $s
$texts = @($res.Cues | ForEach-Object { $_.Text + '@' + $_.Start })
Check 'מילה בקצה החור לא סוגרת אותו: נשלח שוב בחלון מוזז, והדרשה נכנסת' ($res.Recovered -eq 2 -and ($texts -join ',') -match 'הדרשה עצמה@3500' -and $res.Gaps.Count -eq 0) (($texts -join ', ') + " gaps=" + ($res.Gaps -join ','))

# ================= 4ג. נעילת השפה =================
Write-Host 'נעילת השפה'
$he1 = '{"language":"Hebrew","segments":[{"start":1.0,"end":6.0,"text":"משפט ראשון ארוך מספיק כדי להיחשב","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
$s = StartServer @((Resp 200 $he1 $null), (Resp 200 $he1 $null))
$p = NewProvider $s.Port
$r1 = Chunk $p $audio ''
$r2 = Chunk $p $audio ''
StopServer $s
$q0 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-0.bin')))
$q1 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-1.bin')))
Check 'הקטע הראשון: בלי שפה, Whisper מזהה' (-not ($q0 -match 'name="language"')) ''
Check 'מהקטע הבא: השפה שזוהתה ננעלת (turbo תרגם עברית לאנגלית)' ($q1 -match 'name="language"\r\n\r\nhe\r\n') ''
[void]$p.GetType().GetMethod('NewFile').Invoke($p, @())
Check 'קובץ חדש: הנעילה משתחררת' ($p.GetType().GetProperty('Language', [Reflection.BindingFlags]'NonPublic,Instance').GetValue($p, $null) -eq '') ''

# ================= 4ב. עצירה באמצע =================
Write-Host 'עצירה באמצע (שבעה קטעים של 10 שניות)'
$okSeg = '{"segments":[{"start":1.0,"end":3.0,"text":"ראשון","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
$bad = '{"error":{"message":"invalid file"}}'
# קטע 0 מצליח, ואז שלושה כישלונות רצופים שאין טעם לנסות שוב
$s = StartServer @((Resp 200 $okSeg $null), (Resp 400 $bad $null), (Resp 400 $bad $null), (Resp 400 $bad $null))
$p = NewProvider $s.Port
$p.Chunk = 10
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]40000) ([string]'') $null $null))
StopServer $s
Check 'שלושה כישלונות רצופים: נעצר, ולא ממשיך לשאר הקטעים' ($res.Failed -eq 3 -and $res.StoppedAtMs -eq 5000) ("failed=" + $res.Failed + " stoppedAt=" + $res.StoppedAtMs)
Check 'מה שתומלל לפני העצירה נשמר' ($res.Cues.Count -eq 1) ("cues=" + $res.Cues.Count)
Check 'החורים של הקטעים שנכשלו לא נספרים פעמיים' ($res.Gaps.Count -eq 0) ($res.Gaps -join ', ')
Check 'ההודעה היא של השרת' ($res.Error -match 'invalid file') $res.Error

# קטע 0 מצליח. בקטע 1 השעתית של הדגם הראשון נגמרת (20 דקות), והיומית של השני.
$s = StartServer @((Resp 200 $okSeg $null), (Resp 429 $hour 1200), (Resp 429 $day 50000), (Resp 429 $day 50000))
$p = NewProvider $s.Port
$p.Chunk = 10
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]40000) ([string]'') $null $null))
StopServer $s
Check 'מכסה בשני הדגמים: נעצר, ומסמן מכסה' ($res.QuotaOut -and $res.StoppedAtMs -eq 5000) ("quota=" + $res.QuotaOut + " stoppedAt=" + $res.StoppedAtMs)
Check 'ההודעה אומרת מתי אפשר להמשיך (הדגם שמתאפס ראשון)' ($res.Error -match 'בעוד כ-20 דקות') $res.Error
Check 'ובגרסה שעוד לא עברה מכסה: ״בעוד כמה דקות״' ((NewProvider 1).ResetText -match 'כמה דקות') ''

# ״הזיה״ מול עברית ארוכה: compression_ratio גבוה לבד לא מספיק כדי לזרוק
$parse = (T 'OpenAiStt').GetMethod('Parse', $SF)
$heb = 'ואז הגמרא שואלת מאי טעמא ומתרצת שכיון שהוא יוצא לדרך הוא צריך רחמים ולכן תיקנו לו תפילה מיוחדת'
$loop = 'אמן אמן אמן אמן אמן אמן אמן אמן אמן אמן'
$j = '{"segments":[{"start":0,"end":6,"text":"' + $heb + '","avg_logprob":-0.3,"compression_ratio":2.5,"no_speech_prob":0.02},{"start":7,"end":9,"text":"' + $loop + '","avg_logprob":-0.3,"compression_ratio":2.5,"no_speech_prob":0.02}]}'
$pa = New-Object object[] 2; $pa[0] = $j
$pl = @($parse.Invoke($null, $pa))
Check 'משפט עברי ארוך עם יחס דחיסה גבוה - נשאר' (@($pl | Where-Object { $_.Start -lt 6.5 }).Count -ge 1) ("lines=" + $pl.Count)
Check 'חזרה על אותה מילה עם אותו יחס - נזרקת' (-not ($pl | Where-Object { $_.Text -like 'אמן*' })) ''

# ‏Groq: כתוביות לפי משפטים מתוך זרם המילים (0.8.3). הקטעים של Whisper חוצים משפטים, ואחרי
# כמה עשרות שניות הזמנים שלהם בשניות שלמות - הזמנים של המילים מדויקים. המקרים מסרטון אמיתי.
Write-Host 'Groq: כתוביות לפי משפטים מזרם המילים'
function W($t, $s, $e) { return '{"word":"' + $t + '","start":' + $s + ',"end":' + $e + '}' }
$segs = '{"start":0.0,"end":14.4,"text":"Lord Altman of House Open AI. Your Grace, we are close.","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01},' +
        '{"start":63.0,"end":70.0,"text":"Mr. President, our enemies across the water are making progress and I shall deliver it this year.","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}'
$ws = @((W 'Lord' 0.0 7.68), (W 'Altman' 7.68 8.30), (W 'of' 8.30 8.66), (W 'House' 8.66 9.06), (W 'Open' 9.06 9.50), (W 'AI.' 9.50 11.64),
        (W 'Your' 11.64 11.70), (W 'Grace,' 11.70 12.90), (W 'we' 12.90 13.00), (W 'are' 13.00 13.10), (W 'close.' 13.10 14.40),
        (W 'Mr.' 63.50 64.92), (W 'President,' 64.92 65.48), (W 'our' 65.54 65.76), (W 'enemies' 65.76 66.22), (W 'across' 66.22 66.66), (W 'the' 66.66 66.90),
        (W 'water' 66.90 67.20), (W 'are' 67.20 67.80), (W 'making' 67.80 68.12), (W 'progress' 68.12 68.48), (W 'and' 68.60 68.70), (W 'I' 68.70 68.80),
        (W 'shall' 68.80 69.00), (W 'deliver' 69.00 69.30), (W 'it' 69.30 69.40), (W 'this' 69.40 69.60), (W 'year.' 69.60 70.10)) -join ','
$pa = New-Object object[] 2; $pa[0] = '{"segments":[' + $segs + '],"words":[' + $ws + ']}'
$gl = @($parse.Invoke($null, $pa))
Check 'ארבע כתוביות: משפט-משפט, ומשפט ארוך בשתיים' ($gl.Count -eq 4) ("lines=" + $gl.Count + ": " + (($gl | ForEach-Object { $_.Text }) -join ' | '))
if ($gl.Count -eq 4) {
    Check 'קטע שחוצה משפטים מתחלק בסוף המשפט' (($gl[0].Text -eq 'Lord Altman of House Open AI.') -and ($gl[1].Text -eq 'Your Grace, we are close.')) ($gl[0].Text + ' | ' + $gl[1].Text)
    Check 'מילה ש״נמתחה״ על השקט שלפניה מתקצרת (Lord: 0.0 -> 7.18)' ([Math]::Abs($gl[0].Start - 7.18) -lt 0.02) ("start=" + $gl[0].Start)
    Check 'ומילה שנמתחה לתוך ההפסקה שאחריה - גם (AI.: 11.64 -> 10.10)' ([Math]::Abs($gl[0].End - 10.10) -lt 0.02) ("end=" + $gl[0].End)
    Check '״Mr.״ לא סוגר משפט' ($gl[2].Text.StartsWith('Mr. President, our enemies')) $gl[2].Text
    Check 'משפט ארוך מתפצל לפני ״and״, לא באמצע ביטוי' ($gl[3].Text -eq 'and I shall deliver it this year.') $gl[3].Text
    Check 'הזמנים מהמילים, לא מהקטע ששבור לשניות שלמות (68.60, לא 63 או 70)' (([Math]::Abs($gl[3].Start - 68.60) -lt 0.02) -and ([Math]::Abs($gl[2].Start - 64.42) -lt 0.02)) ("" + $gl[2].Start + " / " + $gl[3].Start)
}
# קטע שנזרק כהזיה - המילים שלו נזרקות איתו
$pa[0] = '{"segments":[{"start":0.0,"end":17.0,"text":"Thank you.","avg_logprob":-1.2,"compression_ratio":0.9,"no_speech_prob":0.17},{"start":20.0,"end":21.2,"text":"Real speech here.","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}],' +
         '"words":[' + ((W 'Thank' 0.0 0.5), (W 'you.' 0.5 17.0), (W 'Real' 20.0 20.4), (W 'speech' 20.4 20.8), (W 'here.' 20.8 21.2) -join ',') + ']}'
$gl = @($parse.Invoke($null, $pa))
Check 'הזיה שנזרקה לא חוזרת דרך המילים שלה' (($gl.Count -eq 1) -and ($gl[0].Text -eq 'Real speech here.')) (($gl | ForEach-Object { $_.Text }) -join ' | ')
# מילים שלא תואמות לטקסט - לא סומכים עליהן, וחוזרים לקטעים
$pa[0] = '{"segments":[{"start":1.0,"end":4.0,"text":"one two three four five six","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}],"words":[' + ((W 'one' 1.0 1.2), (W 'two' 1.2 1.4) -join ',') + ']}'
$gl = @($parse.Invoke($null, $pa))
Check 'מילים שלא תואמות לטקסט - הקטע כמו שהוא' (($gl.Count -eq 1) -and ($gl[0].Text -eq 'one two three four five six') -and ($gl[0].Start -eq 1.0)) (($gl | ForEach-Object { "" + $_.Start + " " + $_.Text }) -join ' | ')

# מילה שהמודל לא בטוח בה (0.8.3): ⟦...⟧ יורד מהטקסט ונשמר בכתובית
Write-Host 'מילים שהמודל לא בטוח בהן'
$mk = (T 'Transcribe').GetMethod('MakeCue', $SF)
$c1 = $mk.Invoke($null, @([long]57520, [long]59340, [string]'Only house ⟦Anthropic⟧!'))
Check 'הסימון יורד מהטקסט' ($c1.Text -eq 'Only house Anthropic!') $c1.Text
Check 'והמילה נשמרת בכתובית' ($c1.Doubt -eq 'Anthropic') $c1.Doubt
$c2 = $mk.Invoke($null, @([long]0, [long]1000, [string]'⟦Dots⟧ is a mere ⟦Grokbot⟧ clone ⟧'))
Check 'כמה מילים, וסימן יתום נמחק' (($c2.Text -eq 'Dots is a mere Grokbot clone') -and ($c2.Doubt -eq 'Dots|Grokbot')) ($c2.Text + ' / ' + $c2.Doubt)
$c3 = $mk.Invoke($null, @([long]0, [long]1000, [string]'שורה רגילה'))
Check 'בלי סימון - בלי כלום' (($c3.Text -eq 'שורה רגילה') -and ($c3.Doubt -eq '')) ''

# ניקוד שהמודל הוסיף בתרגום יורד; מקף נשאר (0.8.3: ״גרוקבּוֹט״)
$sn = (T 'Ai').GetMethod('StripNiqqud', $SF)
Check 'ניקוד שנוסף בתרגום יורד' ($sn.Invoke($null, @([string]'גרוקבּוֹט')) -eq 'גרוקבוט') ''
Check 'מקף עברי נשאר (הוא פיסוק, לא ניקוד)' ($sn.Invoke($null, @([string]'אינטליגנציית־על')) -eq 'אינטליגנציית־על') ''
# תשובת התרגום (0.8.5): פריט שחסר מסומן (ונשלח שוב) במקום להישאר בשקט בשפת המקור; ומספור מ-1 לא מזיז הכול בשורה
$ptr = (T 'Ai').GetMethod('ParseTranslation', $SF)
$t0 = $ptr.Invoke($null, (Pack '[{"i":0,"t":"אחת"},{"i":1,"t":"שתיים"}]' ([int]2)))
$t1 = $ptr.Invoke($null, (Pack '[{"i":1,"t":"אחת"},{"i":2,"t":"שתיים"}]' ([int]2)))
$tm = $ptr.Invoke($null, (Pack 'הנה: [{"i":0,"t":"אחת"}]' ([int]2)))
Check 'תרגום: מספור מ-0 כרגיל, ומספור מ-1 מתוקן' (($t0[0] -eq 'אחת') -and ($t0[1] -eq 'שתיים') -and ($t1[0] -eq 'אחת') -and ($t1[1] -eq 'שתיים')) (($t1 -join ',') + ' / ' + ($t0 -join ','))
Check 'תרגום: פריט שחסר - מסומן כחסר (ולא מקבל את המקור בשקט)' (($tm[0] -eq 'אחת') -and ($null -eq $tm[1])) ''
# גוגל חוסם לפעמים בלי נימוק (OTHER) - דגם אחר עובר (0.8.6). עד אז דקה שלמה נשארה בלי כתוביות
$bo = (T 'Ai').GetMethod('BlockedOther', $SF)
$b1 = $bo.Invoke($null, @([string]'{"promptFeedback":{"blockReason":"OTHER"}}'))
$b2 = $bo.Invoke($null, @([string]'{"candidates":[{"finishReason":"OTHER","index":0}]}'))
$b3 = $bo.Invoke($null, @([string]'{"promptFeedback":{"blockReason":"SAFETY"}}'))
$b4 = $bo.Invoke($null, @([string]'{"candidates":[{"content":{"parts":[{"text":"OTHER things"}]},"finishReason":"STOP"}]}'))
Check 'חסימה בלי נימוק (על הבקשה או על התשובה) - מנסים דגם אחר' ($b1 -and $b2) "prompt=$b1 answer=$b2"
Check 'חסימה מנומקת, או תשובה רגילה - לא' ((-not $b3) -and (-not $b4)) "safety=$b3 normal=$b4"

# דוברים (0.8.4): המודל מציין דובר לכל שורה, והוא עובר לכתובית
Write-Host 'דוברים'
$ptl = (T 'Ai').GetMethod('ParseTrLines', $SF)
$pa2 = New-Object object[] 2; $pa2[0] = '[{"s":64.4,"e":65.0,"sp":1,"t":"Mr. President."},{"s":65.1,"e":68.5,"sp":2,"t":"Our enemies are making progress."}]'
$sl = @($ptl.Invoke($null, $pa2))
Check 'מספר הדובר נקרא מהתשובה' (($sl.Count -eq 2) -and ($sl[0].Speaker -eq '1') -and ($sl[1].Speaker -eq '2')) (($sl | ForEach-Object { $_.Speaker }) -join ',')
$allT = [Collections.Generic.List``1].MakeGenericType((T 'Cue')); $allC = [Activator]::CreateInstance($allT)
$lt2 = [Collections.Generic.List``1].MakeGenericType((T 'Ai+TrLine')); $lines2 = [Activator]::CreateInstance($lt2); foreach ($x in $sl) { [void]$lt2.GetMethod('Add').Invoke($lines2, (Pack $x)) }
$ac = (T 'Transcribe').GetMethod('AddChunk', $SF)
$aa = New-Object object[] 6; $aa[0] = $allC; $aa[1] = $lines2; $aa[2] = [int]0; $aa[3] = [double]0; $aa[4] = [int]180; $aa[5] = [long]94000
[void]$ac.Invoke($null, $aa)
Check 'והכתובית יודעת מי הדובר, ובאיזה קטע (המספור מתחיל מחדש בכל קטע)' (($allC.Count -eq 2) -and ($allC[0].Actor -eq '0:1') -and ($allC[1].Actor -eq '0:2')) (($allC | ForEach-Object { $_.Actor }) -join ',')
$allC.Clear(); $aa[2] = [int]3; $aa[3] = [double]165; $aa[5] = [long]400000
[void]$ac.Invoke($null, $aa)
Check 'בקטע הרביעי: ״3:1״' (($allC.Count -ge 1) -and ($allC[0].Actor -eq '3:1')) (($allC | ForEach-Object { $_.Actor }) -join ',')

# ================= 5. בחירת הספק =================
Write-Host 'בחירת הספק'
$stt = T 'Stt'
$pid0 = $stt.GetField('ProviderId', $SF).GetValue($null); $key0 = $stt.GetField('GroqKey', $SF).GetValue($null)
$stt.GetField('ProviderId', $SF).SetValue($null, ''); $stt.GetField('GroqKey', $SF).SetValue($null, '')
$cur = $stt.GetProperty('Current', $SF).GetValue($null, $null)
Check 'בלי בחירה ובלי מפתח ל-Groq: גוגל' ($cur.Id -eq 'gemini') $cur.Id
$stt.GetField('GroqKey', $SF).SetValue($null, 'gsk_x')
$cur = $stt.GetProperty('Current', $SF).GetValue($null, $null)
Check 'בלי בחירה ויש מפתח ל-Groq: Groq' ($cur.Id -eq 'groq') $cur.Id
$stt.GetField('ProviderId', $SF).SetValue($null, 'gemini')
$cur = $stt.GetProperty('Current', $SF).GetValue($null, $null)
Check 'בחירה מפורשת גוברת' ($cur.Id -eq 'gemini') $cur.Id
$stt.GetField('ProviderId', $SF).SetValue($null, $pid0); $stt.GetField('GroqKey', $SF).SetValue($null, $key0)
$gem = $stt.GetProperty('Gemini', $SF).GetValue($null, $null)
Check 'גוגל: קטע של 60 שניות, 12 שניות בין בקשות (כמו קודם)' ($gem.ChunkSec -eq 60 -and $gem.MinGapMs -eq 12000) ''
Check 'גוגל: קטע שחזר ריק לא נשלח שוב (כעשרים בקשות ביום)' (-not $gem.CheapRetry) ''

# ================= התשובה של גוגל: זמנים בכל צורה, ושורות שאין לשכוח =================
# הבאג (23.9.2026, קליפ של שיר, 3 דקות): קטע שהחזיר את התשובה הארוכה מכולם
# נעלם כולו - זמן שלא נקרא כמספר הפך לאפס, ובקטע שאינו הראשון כל השורות נזרקו
# כ״חפיפה״. וקטע אחר נתקע בלולאה: ״מכור עם השם״ 45 פעמים, כל שנייה.
Write-Host 'פירוש התשובה של גוגל'
$aiT = T 'Ai'
$parse = $aiT.GetMethod('ParseTrLines', $SF)
function Parse([string]$raw) { $a = New-Object object[] 2; $a[0] = $raw; $r = $parse.Invoke($null, $a); return ,$r }
$fmt = @(
    @{ n = 'מספרים';                  j = '[{"s":1.5,"e":3,"t":"a"}]';                                  want = 1.5 },
    @{ n = 'מחרוזת של מספר';           j = '[{"s":"12.5","e":"14","t":"a"}]';                             want = 12.5 },
    @{ n = 'דקות:שניות';               j = '[{"s":"0:12.5","e":"0:14","t":"a"}]';                         want = 12.5 },
    @{ n = 'שעות:דקות:שניות,אלפיות';    j = '[{"s":"00:01:05,200","e":"00:01:07,000","t":"a"}]';           want = 65.2 },
    @{ n = 'שמות שדות אחרים (start/text)'; j = '[{"start":12,"end":14,"text":"a"}]';                        want = 12 },
    @{ n = 'עם סיומת s';               j = '[{"s":"7.25s","e":"9s","t":"a"}]';                            want = 7.25 },
    @{ n = 'בתוך גדר קוד';             j = "``````json`n[{`"s`":3,`"e`":4,`"t`":`"a`"}]`n``````";          want = 3 }
)
foreach ($f in $fmt) {
    $r = Parse $f.j
    $ok = $r -ne $null -and $r.Count -eq 1 -and [Math]::Abs($r[0].Start - $f.want) -lt 0.001 -and $r[0].HasTime
    Check ('זמן: ' + $f.n) $ok $(if ($r -ne $null -and $r.Count -gt 0) { 'start=' + $r[0].Start } else { 'null' })
}
$r = Parse '[{"t":"שורה בלי זמן"},{"s":"לא זמן","t":"גם זו"}]'
Check 'שורה בלי זמן לא נזרקת - נשמרת בלי זמן' ($r.Count -eq 2 -and -not $r[0].HasTime -and -not $r[1].HasTime) ('count=' + $r.Count)
$r = Parse '[{"s":4,"t":"בלי סוף"}]'
Check 'בלי זמן סיום: לפי אורך הקריאה, לא 0' ($r[0].End -gt $r[0].Start + 1) ('end=' + $r[0].End)

# הקטע 0:55-1:55 של אותו קליפ חזר (בשחזור) כשורה אחת: ״היי יא יא יא...״ 123 אלף
# תווים, והתשובה נחתכה בתקרת האורך באמצע מחרוזת. כל הקטע נזרק.
Write-Host 'לולאה ותשובה חתוכה'
$unloop = $aiT.GetMethod('Unloop', $SF)
$u = [string]$unloop.Invoke($null, @([string](('היי ' + (('יא ' * 900).Trim())))))
Check 'מילה שחוזרת 900 פעם מתקצרת לארבע' ($u -eq 'היי יא יא יא יא') $u
$u = [string]$unloop.Invoke($null, @([string]'מכור עם השם, השם, השם, השם'))
Check 'חזרה קצרה אמיתית נשארת כמו שהיא' ($u -eq 'מכור עם השם, השם, השם, השם') $u
$salv = $aiT.GetMethod('Salvage', $SF)
$cut = '[{"s":1.0,"e":3.0,"t":"אחת"},{"s":4.0,"e":6.0,"t":"שתיים"},{"s":7.0,"e":9.0,"t":"שלוש יא יא יא יא יא יא יא יא'
$r = $salv.Invoke($null, @([string]$cut))
Check 'תשובה שנחתכה: שתי השורות השלמות נשמרות' ($r -ne $null -and $r.Count -ge 2 -and $r[0].Text -eq 'אחת' -and $r[1].Text -eq 'שתיים') ('count=' + $(if ($r) { $r.Count } else { 0 }))
Check 'והשורה החלקית נשמרת עם הזמן שלה, מקוצרת' ($r.Count -eq 3 -and $r[2].Start -eq 7 -and $r[2].Text -eq 'שלוש יא יא יא יא') $(if ($r -and $r.Count -eq 3) { $r[2].Text } else { '' })
$loopT = $aiT.GetMethod('Looping', $SF)
Check 'לולאה מזוהה (ומפעילה שליחה חוזרת)' ([bool]$loopT.Invoke($null, (Pack (Parse ('[{"s":0,"e":18.9,"t":"היי ' + (('יא ' * 60).Trim()) + '"}]'))))) ''
Check 'שורה רגילה לא נחשבת לולאה' (-not [bool]$loopT.Invoke($null, (Pack (Parse '[{"s":0,"e":3,"t":"מכור עם השם, השם, השם"}]')))) ''

Write-Host 'הרכבת קטע'
$trT = T 'Transcribe'
$lineT = $aiT.GetNestedType('TrLine', [Reflection.BindingFlags]'NonPublic,Public')
$cueT = T 'Cue'
function Lines($spec) {
    $l = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($lineT))
    foreach ($x in $spec) { $o = [Activator]::CreateInstance($lineT); $o.Start = $x[0]; $o.End = $x[1]; $o.Text = $x[2]; $l.Add($o) }
    return ,$l
}
function NewCues { return ,([Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($cueT))) }
$add = $trT.GetMethod('AddChunk', $SF)
$nan = [double]::NaN
# הקטע השני (index 1) מתחיל ב-55 שניות; זמנים שנקראו מ״0:12״ וכו׳ נכנסים במקומם
$all = NewCues
$kept = $add.Invoke($null, (Pack $all (Lines @(@(12.0, 14.0, 'אחת'), @(30.0, 33.0, 'שתיים'))) 1 55.0 60 190000L))
Check 'קטע שני: שורות עם זמן נכנסות במקומן' ($kept -eq 2 -and $all[0].Start -eq 67000 -and -not $all[0].Untimed) ("kept=$kept start=" + $all[0].Start)
$all = NewCues
$kept = $add.Invoke($null, (Pack $all (Lines @(@($nan, $nan, 'אחת'), @($nan, $nan, 'שתיים'), @($nan, $nan, 'שלוש'))) 1 55.0 60 190000L))
$inRange = $true; foreach ($c in $all) { if ($c.Start -lt 60000 -or $c.End -gt 115000 -or -not $c.Untimed) { $inRange = $false } }
Check 'קטע בלי זמנים בכלל: כל השורות נשמרות, משוערות (≈), בתוך הקטע' ($kept -eq 3 -and $inRange) ("kept=$kept")
$all = NewCues
$kept = $add.Invoke($null, (Pack $all (Lines @(,@(95.0, 97.0, 'זמן מחוץ לקטע'))) 1 55.0 60 190000L))
Check 'זמן שלא ייתכן (אחרי סוף הקטע): נשמר כמשוער, לא נזרק' ($kept -eq 1 -and $all[0].Untimed -and $all[0].Start -lt 115000) ("kept=$kept")

Write-Host 'לולאת חזרה'
$collapse = $trT.GetMethod('CollapseRepeats', $SF)
$loop = NewCues
for ($k = 0; $k -lt 45; $k++) { $loop.Add([Activator]::CreateInstance($cueT, @([long](142800 + $k * 1000), [long](143500 + $k * 1000), [string]'מכור עם השם'))) }
$out = $collapse.Invoke($null, (Pack $loop))
$maxLen = 0; foreach ($c in $out) { $maxLen = [Math]::Max($maxLen, $c.End - $c.Start) }
Check '45 חזרות צמודות: כמה כתוביות של עד 7 שניות, לא 45' ($out.Count -ge 4 -and $out.Count -le 8 -and $maxLen -le 7000) ("count=" + $out.Count + " max=" + $maxLen)
$song = NewCues
foreach ($x in @(@(0, 2500, 'בקרוב ממש'), @(5000, 7500, 'בקרוב ממש'), @(8000, 9000, 'אחרת'))) { $song.Add([Activator]::CreateInstance($cueT, @([long]$x[0], [long]$x[1], [string]$x[2]))) }
$out = $collapse.Invoke($null, (Pack $song))
Check 'פזמון שחוזר אחרי הפסקה אמיתית נשאר שתי כתוביות' ($out.Count -eq 3) ("count=" + $out.Count)

Write-Host 'קול בלי כתוביות (Recover) - על נתונים שנמדדו ב-24.9.2026'
$holes = $trT.GetMethod('Holes', $SF)
$merge = $trT.GetMethod('Merge', $SF)
function Sil($spec) { $l = New-Object 'System.Collections.Generic.List[double[]]'; foreach ($x in $spec) { $l.Add([double[]]@($x[0], $x[1])) }; return ,$l }
# שיר של 56 שניות: מוזיקה רצופה, בלי שקט; הכתוביות נגמרות ב-26
$song = Lines @(@(1.5, 3.0, 'שוב אתה בא'), @(3.0, 16.0, 'תן לי מילה אחת גדולה'), @(21.0, 26.0, 'היא עושה בנו להט'))
$h = $holes.Invoke($null, (Pack $song (Sil @()) 0 56.1))
Check 'שיר: קול עד הסוף וכתוביות עד 26 - חור בסוף הקטע' ($h.Count -eq 1 -and $h[0][0] -eq 26.0 -and $h[0][2] -eq 1) ('holes=' + $h.Count)
$h = $holes.Invoke($null, (Pack $song (Sil @(,@(26.2, 56.1))) 0 56.1))
Check 'אותו קטע, אבל אחרי 26 שקט - אין חור' ($h.Count -eq 0) ('holes=' + $h.Count)
$mid = Lines @(@(0.0, 20.0, 'עד כאן'), @(35.0, 40.0, 'ומכאן'))
$h = $holes.Invoke($null, (Pack $mid (Sil @(,@(40.5, 60.0))) 0 60.0))
Check 'חור באמצע (20-35) שיש בו קול' ($h.Count -eq 1 -and $h[0][0] -eq 20.0 -and $h[0][1] -eq 35.0 -and $h[0][2] -eq 0) ('holes=' + $h.Count)
$h = $holes.Invoke($null, (Pack (Lines @(,@(12.0, 60.0, 'x'))) (Sil @()) 1 60.0))
Check 'קטע שני: חמש השניות הראשונות כבר כוסו בחפיפה - שבע שניות אינן חור' ($h.Count -eq 0) ('holes=' + $h.Count)

# הנאום בכנסת: התמלול המלא ״כיווץ״ את הזמנים. הזנב (מ-45) תומלל שוב לבד.
$knesset = Lines @(@(29.5, 33.5, 'אז אני חש חובה לנצל את זכות הדיבור שלי בכדי למחות.'), @(33.5, 37.5, 'אני עומד פה בכנסת, בפרלמנט בשלטון של יהודים,'),
    @(37.5, 41.5, 'ומוחה על הפגיעה בלומדי התורה וזועק למקבלי ההחלטות:'), @(41.5, 45.0, 'תתעשתו כי אתם משחקים באש! תפסיקו,'), @(45.0, 48.0, 'פשוט תפסיקו לרדוף את לומדי התורה!'))
$tail = Lines @(@(0.0, 2.14, 'יהודים ומוחה'), @(2.14, 4.6, 'על הפגיעה בלומדי התורה'), @(4.6, 7.48, 'וזועק'), @(7.48, 10.14, 'למקבלי ההחלטות, תתעשתו'),
    @(10.14, 12.34, 'כי אתם משחקים באש.'), @(12.34, 14.28, 'תפסיקו, פשוט תפסיקו'), @(14.28, 16.54, 'לרדוף את לומדי התורה, תודה.'))
$out = $merge.Invoke($null, (Pack $knesset $tail 45.0 $true 61.2))
$fire = $null; foreach ($l in $out) { if ($l.Text -like 'תתעשתו*') { $fire = $l } }
Check 'נאום: הזמנים נמתחים - ״תתעשתו״ עובר מ-41.5 אל סביב 52.5 (במקום באמת)' ($fire -ne $null -and $fire.Start -gt 50 -and $fire.Start -lt 56) ('start=' + $fire.Start)
$dups = 0; foreach ($l in $out) { if ($l.Text -like '*משחקים באש*') { $dups++ } }
Check 'נאום: בלי כפילות - ״משחקים באש״ מופיע פעם אחת' ($dups -eq 1) ('count=' + $dups + ' lines=' + $out.Count)
$out = $merge.Invoke($null, (Pack (Lines @(@(1.5, 3.0, 'שוב אתה בא'), @(21.0, 26.0, 'היא עושה בנו להט'))) (Lines @(@(1.0, 4.0, 'אז תבטיח לי'), @(4.0, 6.5, 'שתשמור עליי'))) 25.0 $true 56.1))
$last = $out[$out.Count - 1]
Check 'שיר: המודל דילג - השורות החדשות נכנסות לחור (29-31.5), והקודמות לא זזות' ($out.Count -eq 4 -and $last.Start -eq 29.0 -and $out[1].Start -eq 21.0) ('count=' + $out.Count + ' last=' + $last.Start)
$out = $merge.Invoke($null, (Pack (Lines @(@(10.0, 14.0, 'שורה'), @(18.0, 22.0, 'תבטיח לי'))) (Lines @(@(1.0, 4.0, 'תבטיח לי'), @(5.0, 8.0, 'משהו חדש'))) 25.0 $true 56.1))
$still = $out[1]
Check 'פזמון שחוזר בזנב (עוגן אחד) לא נחשב להתכווצות: שום זמן לא נמתח' ($still.Start -eq 18.0 -and $out.Count -eq 4) ('start=' + $still.Start + ' count=' + $out.Count)

# ================= סגירת חלון באמצע עבודה (0.8.5) =================
# עד 0.8.5 ‏× או Esc סגרו את החלון, והתמלול או התרגום המשיכו ברקע עד הסוף - צרכו מכסה, והתוצאה נזרקה
Write-Host 'סגירה באמצע'
Add-Type -AssemblyName System.Windows.Forms
$IF = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function Closing($dlg) {
    $e = New-Object System.Windows.Forms.FormClosingEventArgs ([System.Windows.Forms.CloseReason]::UserClosing), $false
    [void]$dlg.GetType().GetMethod('OnFormClosing', $IF).Invoke($dlg, (Pack $e))
    return $e.Cancel
}
$run = [Activator]::CreateInstance((T 'TranscribeRunDlg'), (Pack (NewProvider 1) 'x.mp3' ([long]60000) ''))
$c1 = Closing $run
$stop1 = $run.GetType().GetField('_cancel', $IF).GetValue($run)
Check 'תמלול: ‏× ראשון = ״עצירה״ - החלון נשאר עד שמה שתומלל חוזר' ($c1 -eq $true -and $stop1 -eq $true) ("cancel=$c1 stop=$stop1")
$c2 = Closing $run
Check 'תמלול: ‏× שני סוגר מיד' ($c2 -eq $false) "cancel=$c2"
$run.Dispose()
$run = [Activator]::CreateInstance((T 'TranscribeRunDlg'), (Pack (NewProvider 1) 'x.mp3' ([long]60000) ''))
$run.GetType().GetField('_finished', $IF).SetValue($run, $true)
Check 'תמלול: כשהעבודה נגמרה - נסגר כרגיל' ((Closing $run) -eq $false) ''
$run.Dispose()
$tl = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType((T 'Cue')))
$ai = [Activator]::CreateInstance((T 'AiRunDlg'), (Pack $tl 'עברית' ''))
[void](Closing $ai)
Check 'תרגום: ‏× עוצר את העבודה ברקע' ($ai.GetType().GetField('_cancel', $IF).GetValue($ai) -eq $true) ''
$ai.Dispose()
# חלון התמלול (0.8.5): ״המפתח שכבר יש לכם״ רק למי שיש; והרקע מהתמלול הקודם של אותו סרט כבר בשדה
$mi0 = [Activator]::CreateInstance((T 'MediaInfo')); $mi0.DurationSec = 60
(T 'Ai').GetField('Key', $SF).SetValue($null, '')
$td = [Activator]::CreateInstance((T 'TranscribeDlg'), (Pack $mi0 ([int]0) 'שיעור של הרב כהן'))
$gsub = [string]$td.GetType().GetField('_optGoogle', $IF).GetValue($td).Sub
Check 'תמלול: בלי מפתח לגוגל - לא ״המפתח שכבר יש לכם״; והרקע הקודם כבר בשדה' ($gsub -notmatch 'שכבר יש' -and $td.Context -eq 'שיעור של הרב כהן') ($gsub + ' | ' + $td.Context)
$rg = $td.GetType().GetField('_range', $IF).GetValue($td)
Check 'תמלול: בלי קטע מסומן - המתג ״רק חלק מהסרט״ כבוי ומסביר איך מסמנים' ((-not $rg.Enabled) -and $rg.Text -match 'מסמנים' -and $td.ToMs -eq 0) $rg.Text
$td.Dispose()
$td = [Activator]::CreateInstance((T 'TranscribeDlg'), (Pack $mi0 ([int]5) '' ([long]20000) ([long]45000)))
$rg = $td.GetType().GetField('_range', $IF).GetValue($td)
$rg.Checked = $true
$rp = $td.GetType().GetField('_replace', $IF).GetValue($td)
Check 'תמלול: קטע מסומן - המתג פעיל, עם הזמנים; ו״למחוק״ מדבר על הקטע בלבד' ($rg.Enabled -and $td.FromMs -eq 20000 -and $td.ToMs -eq 45000 -and $rp.Text -match 'בקטע') ($rg.Text + ' | ' + $rp.Text)
$td.Dispose()

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
