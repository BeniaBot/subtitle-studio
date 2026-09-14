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
# צפוי: 45 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\SubtitleStudio.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
function Pack { $a = New-Object object[] $args.Count; for ($i=0;$i -lt $args.Count;$i++){ $v=$args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i]=$v }; return ,$a }
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
$s = StartServer @((Resp 200 $c0 $null), (Resp 200 $c1 $null))
$p = NewProvider $s.Port
$p.Chunk = 25
$trT = T 'Transcribe'
$run = $null
foreach ($m in $trT.GetMethods($SF)) { if ($m.Name -eq 'Run' -and $m.GetParameters().Count -eq 6) { $run = $m } }
$sw = [Diagnostics.Stopwatch]::StartNew()
$res = $run.Invoke($null, (Pack $p ([string]$media) ([long]40000) ([string]'') $null $null))
StopServer $s
$texts = @($res.Cues | ForEach-Object { $_.Text + '@' + $_.Start })
Check 'שלוש כתוביות: הכפילות בחפיפה נזרקה' ($res.Cues.Count -eq 3) ($texts -join ', ')
Check 'הזמנים מוזזים לפי תחילת הקטע' (($texts -join ',') -eq 'אחת@1000,בגבול@21000,שתיים@27000') ($texts -join ',')
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

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
