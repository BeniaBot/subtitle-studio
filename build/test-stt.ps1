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
# צפוי: 74 בדיקות.
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
 {"id":4,"start":28.2,"end":29.5,"text":" תודה רבה.","avg_logprob":-0.25,"compression_ratio":0.9,"no_speech_prob":0.03}
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
$pm = [regex]::Match($req, 'name="prompt"\r\n\r\n([^\r]*)\r\n')
Check 'הרקע נשלח כרמז, חתוך ל-100 תווים' ($pm.Success -and $pm.Groups[1].Value.Length -eq 100 -and $ctx.StartsWith($pm.Groups[1].Value)) ("len=" + $pm.Groups[1].Value.Length)
Check 'הקול עצמו בגוף, בייט-בייט' ($bytes.Length -gt $audio.Length -and $req -match 'filename="chunk\.mp3"\r\nContent-Type: audio/mpeg') ("body=" + $bytes.Length)
Check 'שורה ראשונה: טקסט בלי רווח מוביל, בזמנים שלה' ($lines.Count -gt 0 -and $lines[0].Text -eq 'שלום לכולם' -and $lines[0].Start -eq 0 -and $lines[0].End -eq 3.2) ''
Check '״תודה רבה״ בשקט (Whisper לא בטוח שהיה דיבור) - נזרק' (-not ($lines | Where-Object { $_.Start -eq 5.0 })) ''
Check 'לולאת חזרה (compression_ratio 3.1) - נזרקת' (-not ($lines | Where-Object { $_.Text -like 'כן כן*' })) ''
Check '״תודה רבה״ שנאמר באמת - נשאר' (@($lines | Where-Object { $_.Start -eq 28.2 }).Count -eq 1) ''
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
$fix0 = '{"segments":[{"start":5.0,"end":7.0,"text":"באמצע","avg_logprob":-0.2,"compression_ratio":1,"no_speech_prob":0.01}]}'
$none = '{"segments":[]}'
$s = StartServer @((Resp 200 $c0 $null), (Resp 200 $fix0 $null), (Resp 200 $c1 $null), (Resp 200 $none $null))
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
Check 'שתי בקשות תיקון - אחת לכל קטע שנשאר בו קול בלי כתוביות' ($res.Recovered -eq 2) ("recovered=" + $res.Recovered)
Check 'שם השירות בתוצאה' ($res.ProviderName -eq 'Groq') $res.ProviderName
Check 'בלי שגיאה ובלי חורים' ($res.Error -eq $null -and $res.Gaps.Count -eq 0 -and $res.Failed -eq 0) ("error=" + $res.Error)

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

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
