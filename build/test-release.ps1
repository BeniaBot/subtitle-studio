# קריאת תשובת GitHub - החלק שכל מנגנון העדכון תלוי בו.
#
# הגרסה הקודמת של Parse נשענה על ביטוי רגולרי אחד שנמתח על שלושה שדות
# של אותו נכס (name -> size -> browser_download_url) עם תקרה של 900 תווים.
# גיטהאב הגדיל את אובייקט ה-uploader, הפער האמיתי הגיע ל-1012, והביטוי
# הפסיק להתאים - בלי שום שגיאה ובלי שום סימן. התוכנה המשיכה להכריז על
# גרסה חדשה ולהגיד "בשחרור הזה לא צורף קובץ EXE" לכל מי שלחץ לעדכן.
#
# הבדיקה רצה מול תשובות בנויות (בלי רשת) וגם מול השחרור החי, כדי ששינוי
# בצד של גיטהאב ייתפס כאן ולא אצל המשתמש.
#
#   powershell -ExecutionPolicy Bypass -File build\test-release.ps1
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\Subtext.exe'
if (-not (Test-Path $exe)) { Write-Host 'no exe - run build.cmd first'; exit 1 }

$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  ok   {0}   {1}" -f $name, $detail) }
    else { $script:fail++; Write-Host ("  FAIL {0}   {1}" -f $name, $detail) -ForegroundColor Red }
}

$updT = T 'Updater'
$parse = $updT.GetMethod('Parse', $ST)
$relT = T 'Updater+Release'

function DoParse($json) {
    $a = New-Object object[] 2
    $a[0] = [string]$json
    $r = $parse.Invoke($null, $a)
    return @{ Rel = $r; Err = $a[1] }
}
function F($rel, $name) { $relT.GetField($name).GetValue($rel) }

function Uploader($login) {
    $u = '{"login":"' + $login + '","id":123456789,"node_id":"MDQ6VXNlcjE=",'
    foreach ($k in @('avatar_url','gravatar_id','url','html_url','followers_url','following_url',
                     'gists_url','starred_url','subscriptions_url','organizations_url','repos_url',
                     'events_url','received_events_url')) {
        $u += '"' + $k + '":"https://api.github.com/users/' + $login + '/' + $k + '",'
    }
    return $u + '"type":"User","site_admin":false}'
}
function Asset($name, $size, $login) {
    return '{"url":"https://api.github.com/repos/BeniaBot/subtitle-studio/releases/assets/1","id":1,' +
           '"node_id":"RA_kwDO","name":"' + $name + '","label":null,' +
           '"uploader":' + (Uploader $login) + ',' +
           '"content_type":"application/x-msdownload","state":"uploaded",' +
           '"size":' + $size + ',"download_count":7,' +
           '"created_at":"2026-09-01T10:00:00Z","updated_at":"2026-09-01T10:01:00Z",' +
           '"browser_download_url":"https://github.com/BeniaBot/subtitle-studio/releases/download/v9.9.9/' + $name + '"}'
}
function Release($assets, $tag, $body) {
    return '{"url":"https://api.github.com/repos/BeniaBot/subtitle-studio/releases/1","tag_name":"' + $tag + '",' +
           '"name":"' + $tag + '","draft":false,"prerelease":false,' +
           '"body":"' + $body + '","assets":[' + ($assets -join ',') + ']}'
}

Write-Host 'HDR_FIXED'

$both = Release @((Asset 'Subtext-Setup.exe' 41000000 'BeniaBot'),
                  (Asset 'Subtext.exe' 38000000 'BeniaBot')) '0.9.9' 'notes here'
$p = DoParse $both
Check 'T_BOTH_NOERR' ($null -eq $p.Err -and $null -ne $p.Rel) ("err=" + $p.Err)
Check 'T_BOTH_VER'   ((F $p.Rel 'Version') -eq '0.9.9') (F $p.Rel 'Version')
Check 'T_BOTH_PORT'  ((F $p.Rel 'Url') -like '*/Subtext.exe') (F $p.Rel 'Url')
Check 'T_BOTH_SETUP' ((F $p.Rel 'SetupUrl') -like '*/Subtext-Setup.exe') (F $p.Rel 'SetupUrl')
Check 'T_BOTH_SZ1'   ((F $p.Rel 'Size') -eq 38000000) (F $p.Rel 'Size')
Check 'T_BOTH_SZ2'   ((F $p.Rel 'SetupSize') -eq 41000000) (F $p.Rel 'SetupSize')

$long = Release @((Asset 'Subtext-Setup.exe' 41000000 ('a' * 120)),
                  (Asset 'Subtext.exe' 38000000 ('a' * 120))) '1.0.0' 'x'
$p = DoParse $long
Check 'T_LONGUPLOADER' ((F $p.Rel 'Url').Length -gt 0 -and (F $p.Rel 'SetupUrl').Length -gt 0) ("url=" + (F $p.Rel 'Url').Length + " setup=" + (F $p.Rel 'SetupUrl').Length)

$onlySetup = Release @((Asset 'Subtext-Setup.exe' 41000000 'BeniaBot')) '1.0.1' 'x'
$p = DoParse $onlySetup
Check 'T_ONLYSETUP_HAS' ((F $p.Rel 'SetupUrl').Length -gt 0) (F $p.Rel 'SetupUrl')
Check 'T_ONLYSETUP_NOPORT' ((F $p.Rel 'Url') -eq '') ("'" + (F $p.Rel 'Url') + "'")

$onlyPort = Release @((Asset 'Subtext.exe' 38000000 'BeniaBot')) '1.0.2' 'x'
$p = DoParse $onlyPort
Check 'T_ONLYPORT_HAS' ((F $p.Rel 'Url').Length -gt 0) (F $p.Rel 'Url')
Check 'T_ONLYPORT_NOSETUP' ((F $p.Rel 'SetupUrl') -eq '') ("'" + (F $p.Rel 'SetupUrl') + "'")

$noise = Release @((Asset 'SHA256SUMS.txt' 200 'BeniaBot'),
                   (Asset 'source.zip' 900 'BeniaBot'),
                   (Asset 'Subtext.exe' 38000000 'BeniaBot')) '1.0.3' 'x'
$p = DoParse $noise
Check 'T_NOISE' ((F $p.Rel 'Url') -like '*/Subtext.exe') (F $p.Rel 'Url')

$two = Release @((Asset 'Subtext-Setup.exe' 41000000 'BeniaBot'),
                 (Asset 'Subtext-Setup-x86.exe' 39000000 'BeniaBot')) '1.0.4' 'x'
$p = DoParse $two
Check 'T_FIRSTSETUPWINS' ((F $p.Rel 'SetupUrl') -like '*/Subtext-Setup.exe') (F $p.Rel 'SetupUrl')

$evil = (Release @((Asset 'Subtext.exe' 38000000 'BeniaBot')) '1.0.5' 'x').Replace('https://github.com/BeniaBot/subtitle-studio/releases/download/v9.9.9/', 'https://evil.example.com/')
$p = DoParse $evil
Check 'T_FOREIGNURL' ((F $p.Rel 'Url') -eq '') ("'" + (F $p.Rel 'Url') + "'")

# מאגר אחר בגיטהאב עצמו - גם הוא נדחה
$other = (Release @((Asset 'Subtext.exe' 38000000 'BeniaBot')) '1.0.7' 'x').Replace(
        'https://github.com/BeniaBot/subtitle-studio/releases/download/v9.9.9/',
        'https://github.com/someone/else/releases/download/v9.9.9/')
$p = DoParse $other
Check 'T_OTHERREPO' ((F $p.Rel 'Url') -eq '') ("'" + (F $p.Rel 'Url') + "'")

# http במקום https נדחה
$plain = (Release @((Asset 'Subtext.exe' 38000000 'BeniaBot')) '1.0.8' 'x').Replace(
        'https://github.com/BeniaBot/', 'http://github.com/BeniaBot/')
$p = DoParse $plain
Check 'T_PLAINHTTP' ((F $p.Rel 'Url') -eq '') ("'" + (F $p.Rel 'Url') + "'")

$esc = Release @((Asset 'Subtext.exe' 1 'BeniaBot')) '1.0.6' 'line one\nline two \"quoted\" and a backslash \\\\ end'
$p = DoParse $esc
$notes = F $p.Rel 'Notes'
Check 'T_NOTES_NL'    ($notes.Contains("`n")) ''
Check 'T_NOTES_QUOTE' ($notes.Contains('"quoted"')) ''
Check 'T_NOTES_SLASH' ($notes.Contains('\')) $notes

foreach ($bad in @('', 'not json at all', '{', '[]', '{"assets":[]}', '{"tag_name":"1.0","assets":"nope"}')) {
    $ok = $true; $d = ''
    try { $r = DoParse $bad; $d = 'err=' + $r.Err }
    catch { $ok = $false; $d = 'EXC_' + $_.Exception.InnerException.GetType().Name }
    Check ("T_BADINPUT '" + $bad + "'") $ok $d
}

Write-Host ''
Write-Host 'HDR_LIVE'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $wc = New-Object Net.WebClient
    $wc.Headers.Add('User-Agent', 'Subtext-test')
    $wc.Headers.Add('Accept', 'application/vnd.github+json')
    $live = $wc.DownloadString('https://api.github.com/repos/BeniaBot/subtitle-studio/releases/latest')
    $p = DoParse $live
    $v = F $p.Rel 'Version'; $u = F $p.Rel 'Url'; $s = F $p.Rel 'SetupUrl'
    Check 'T_LIVE_READ'  ($null -ne $p.Rel) ("err=" + $p.Err)
    # ה-v של התגית חייב להיות מקולף כאן - הוא נכנס גם לשם הקובץ הזמני
    Check 'T_LIVE_VER'   ($v -match '^\d+\.\d+\.\d+$') $v
    Check 'T_LIVE_PORT'  ($u -like 'https://github.com/*/releases/download/*') $u
    Check 'T_LIVE_SETUP' ($s -like 'https://github.com/*/releases/download/*') $s
    Check 'T_LIVE_SIZES' ((F $p.Rel 'Size') -gt 1000000 -and (F $p.Rel 'SetupSize') -gt 1000000) ((F $p.Rel 'Size').ToString() + ' / ' + (F $p.Rel 'SetupSize').ToString())
    # גיטהאב מפרסם טביעה לכל קובץ; בלעדיה ההורדה נבדקת רק לפי אורך
    Check 'T_LIVE_DIGEST' ((F $p.Rel 'Sha256') -match '^[0-9A-F]{64}$' -and (F $p.Rel 'SetupSha256') -match '^[0-9A-F]{64}$') (F $p.Rel 'Sha256')
} catch {
    Write-Host ("  --   HDR_NONET: " + $_.Exception.Message)
}

# ---------- 0.8.0: הורדה שנבדקת, וגשר לשם המאגר ----------
# עד 0.8.0 ההורדה נבדקה רק לפי ״גדול מ-1MB״: קובץ שנחתך באמצע, או דף חסימה של
# סינון, היו עוברים. והמאגר עתיד להיקרא subtext: הגרסה הזאת חייבת לקבל את שני השמות.
Write-Host ''
Write-Host 'HDR_FETCH'
$ours = $updT.GetMethod('IsOurDownload', $ST)
Check 'T_REPO_OLD'   ($ours.Invoke($null, @([string]'https://github.com/BeniaBot/subtitle-studio/releases/download/v1/a.exe'))) ''
Check 'T_REPO_NEW'   ($ours.Invoke($null, @([string]'https://github.com/BeniaBot/subtext/releases/download/v1/a.exe'))) ''
Check 'T_REPO_OTHER' (-not $ours.Invoke($null, @([string]'https://github.com/Evil/subtext/releases/download/v1/a.exe')) -and
                      -not $ours.Invoke($null, @([string]'https://github.com/BeniaBot/subtext-evil/releases/download/v1/a.exe'))) ''

$dj = Release ((Asset 'Subtext.exe' 5 'x') -replace '"size":5', '"size":5,"digest":"sha256:ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12ab12"') 'v9.9.9' 'x'
$dp = DoParse $dj
Check 'T_DIGEST_READ' ((F $dp.Rel 'Sha256') -eq 'AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12AB12') (F $dp.Rel 'Sha256')

$srv = {
    param($port, $status, $ctype, [byte[]]$body, $declared)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (-not $l.Pending() -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        if (-not $l.Pending()) { return }
        $c = $l.AcceptTcpClient(); $ns = $c.GetStream(); $ns.ReadTimeout = 15000
        $ms = New-Object IO.MemoryStream; $buf = New-Object byte[] 8192
        while ($true) { $n = $ns.Read($buf, 0, $buf.Length); if ($n -le 0) { break }; $ms.Write($buf, 0, $n); if ([Text.Encoding]::ASCII.GetString($ms.ToArray()).Contains("`r`n`r`n")) { break } }
        $head = "HTTP/1.1 $status X`r`nContent-Type: $ctype`r`nContent-Length: $declared`r`nConnection: close`r`n`r`n"
        $hb = [Text.Encoding]::ASCII.GetBytes($head)
        try { $ns.Write($hb, 0, $hb.Length); $ns.Write($body, 0, $body.Length); $ns.Flush() } catch { }
        $c.Close()
    } finally { $l.Stop() }
}
function FreePort { $t = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), 0; $t.Start(); $p = $t.LocalEndpoint.Port; $t.Stop(); return $p }
$fetch = $updT.GetMethod('Fetch', $ST)
$tmpDl = Join-Path $env:TEMP ('ss-fetch-' + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.exe')
function TryFetch($status, $ctype, [byte[]]$body, $declared, $expectSize, $expectSha) {
    $port = FreePort
    $ps = [powershell]::Create()
    [void]$ps.AddScript($srv).AddArgument($port).AddArgument($status).AddArgument($ctype).AddArgument($body).AddArgument($declared)
    $h = $ps.BeginInvoke(); Start-Sleep -Milliseconds 300
    $a = New-Object object[] 6
    $a[0] = [string]"http://127.0.0.1:$port/x.exe"; $a[1] = [string]$tmpDl; $a[2] = [long]$expectSize; $a[3] = [string]$expectSha; $a[4] = [long]0; $a[5] = [long]0
    $err = $fetch.Invoke($null, $a)
    if (-not $h.AsyncWaitHandle.WaitOne(20000)) { $ps.Stop() }
    try { [void]$ps.EndInvoke($h) } catch { }
    $ps.Dispose()
    Remove-Item $tmpDl -ErrorAction SilentlyContinue
    return [string]$err
}
$exeBody = New-Object byte[] 1200000; (New-Object Random 7).NextBytes($exeBody)
$sha = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($exeBody))).Replace('-', '')
$html = [Text.Encoding]::UTF8.GetBytes('<html><body>blocked</body></html>')

$e = TryFetch 200 'application/octet-stream' $exeBody $exeBody.Length $exeBody.Length $sha
Check 'T_FETCH_OK'        ($e -eq '') $e
$e = TryFetch 200 'application/octet-stream' $exeBody $exeBody.Length $exeBody.Length ('0' * 64)
Check 'T_FETCH_BADHASH'   ($e.Contains('לא זהה')) $e
$e = TryFetch 200 'text/html' $html $html.Length 0 ''
Check 'T_FETCH_BLOCKPAGE' ($e.Contains('סינון')) $e
$e = TryFetch 418 'text/html' $html $html.Length 0 ''
Check 'T_FETCH_418'       ($e.Contains('סינון')) $e
$e = TryFetch 200 'application/octet-stream' $exeBody ($exeBody.Length + 500000) 0 ''
Check 'T_FETCH_CUT'       ($e.Contains('נקטעה') -or $e.Contains('נותק')) $e
$e = TryFetch 200 'application/octet-stream' $exeBody $exeBody.Length ($exeBody.Length + 1) ''
Check 'T_FETCH_SIZE'      ($e.Contains('נקטעה')) $e
$e = TryFetch 404 'text/plain' ([Text.Encoding]::ASCII.GetBytes('nope')) 4 0 ''
Check 'T_FETCH_404'       ($e.Contains('לא נמצא')) $e

# ================= העדכון הקטן (0.8.8) =================
# חלק התוכנה בלבד, דחוס; המנוע והחלק האחרון מודבקים מהקובץ הישן; התוצאה חייבת להיות בדיוק הקובץ שפורסם.
Write-Host 'HDR_SLIM'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
$rtT = T 'Runtime'
$work = Join-Path $env:TEMP ('ss-slim-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Force $work | Out-Null
function Sha([byte[]]$b) { return ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($b))).Replace('-', '') }
function Rand([int]$n, [int]$seed) { $b = New-Object byte[] $n; (New-Object Random $seed).NextBytes($b); return ,$b }
function Pack([byte[]]$body) { $h = [byte[]][char[]]'FFP1'; $sz = [BitConverter]::GetBytes([long]123456789); return ,([byte[]]($h + $sz + $body)) }
function Trailer([byte[]]$pack) {
    $m = [byte[]][char[]]'SUBTEXT-ENGINE-1'
    $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($pack)
    return ,([byte[]]($m + [BitConverter]::GetBytes([long]$pack.Length) + $hash + [BitConverter]::GetBytes([long]0)))
}
function Gz([byte[]]$b) { $ms = New-Object IO.MemoryStream; $z = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress, $true); $z.Write($b, 0, $b.Length); $z.Dispose(); return ,$ms.ToArray() }
function WriteFile($path, [byte[]]$b) { [IO.File]::WriteAllBytes($path, $b) }

$appOld = Rand 300000 11; $appNew = Rand 420000 12
$pack = Pack (Rand 600000 13); $tail = Trailer $pack
$self = Join-Path $work 'self.exe';   WriteFile $self ([byte[]]($appOld + $pack + $tail))
$full = [byte[]]($appNew + $pack + $tail)
$gz = Gz $appNew

$readTrailer = $rtT.GetMethod('ReadTrailer', $ST)
function RT($path) { $a = New-Object object[] 2; $a[0] = [string]$path; $r = $readTrailer.Invoke($null, $a); return @{ R = $r; Id = $a[1] } }
$t1 = RT $self
Check 'T_TRAILER_READ' ($t1.R -ne $null -and $t1.R[0] -eq $appOld.Length -and $t1.R[1] -eq $pack.Length -and $t1.Id -eq (Sha $pack).ToLower()) ("offset=" + $(if ($t1.R) { $t1.R[0] } else { 'null' }) + " id=" + $t1.Id)
$junk = Join-Path $work 'junk.exe'; WriteFile $junk (Rand 50000 14)
$cut = Join-Path $work 'cut.exe';   WriteFile $cut ([byte[]]($appOld + $pack[0..1000] + $tail))
$bad = Join-Path $work 'bad.exe';   $bt = [byte[]]$tail.Clone(); $bt[16] = 0xFF; $bt[23] = 0x7F; WriteFile $bad ([byte[]]($appOld + $pack + $bt))
Check 'T_TRAILER_NONE' ((RT $junk).R -eq $null -and (RT $cut).R -eq $null -and (RT $bad).R -eq $null -and (RT (Join-Path $work 'nope.exe')).R -eq $null) 'random / cut engine / insane length / missing'

# הקובץ האמיתי: המנוע בסופו, והתוכנה קוראת ממנו את גודל ffmpeg ואת הזהות
$td = RT $exe
Check 'T_TRAILER_DIST' ($td.R -ne $null -and $td.Id.Length -eq 64) ("dist id=" + $td.Id.Substring(0, [Math]::Min(12, $td.Id.Length)))

# שרת מדומה לכמה בקשות, לפי נתיב
$srvMany = {
    param($port, $routes, $count, $slowMs)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    try {
        for ($i = 0; $i -lt $count; $i++) {
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            while (-not $l.Pending() -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 30 }
            if (-not $l.Pending()) { return }
            $c = $l.AcceptTcpClient(); $ns = $c.GetStream(); $ns.ReadTimeout = 15000
            $ms = New-Object IO.MemoryStream; $buf = New-Object byte[] 8192
            while ($true) { $n = $ns.Read($buf, 0, $buf.Length); if ($n -le 0) { break }; $ms.Write($buf, 0, $n); if ([Text.Encoding]::ASCII.GetString($ms.ToArray()).Contains("`r`n`r`n")) { break } }
            $req = [Text.Encoding]::ASCII.GetString($ms.ToArray()); $path = ($req -split ' ')[1]
            $body = $routes[$path]
            if ($body -eq $null) { $head = "HTTP/1.1 404 X`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"; $hb = [Text.Encoding]::ASCII.GetBytes($head); $ns.Write($hb, 0, $hb.Length) }
            else {
                $head = "HTTP/1.1 200 OK`r`nContent-Type: application/octet-stream`r`nContent-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n"
                $hb = [Text.Encoding]::ASCII.GetBytes($head)
                try {
                    $ns.Write($hb, 0, $hb.Length)
                    if ($slowMs -gt 0) { for ($o = 0; $o -lt $body.Length; $o += 4096) { $ns.Write($body, $o, [Math]::Min(4096, $body.Length - $o)); Start-Sleep -Milliseconds $slowMs } }
                    else { $ns.Write($body, 0, $body.Length) }
                    $ns.Flush()
                } catch { }
            }
            $c.Close()
        }
    } finally { $l.Stop() }
}
function Serve($routes, $count, $slowMs) {
    $port = FreePort
    $ps = [powershell]::Create()
    [void]$ps.AddScript($srvMany).AddArgument($port).AddArgument($routes).AddArgument($count).AddArgument($slowMs)
    $h = $ps.BeginInvoke(); Start-Sleep -Milliseconds 300
    return @{ Port = $port; Ps = $ps; H = $h }
}
function Done($s) { if (-not $s.H.AsyncWaitHandle.WaitOne(20000)) { $s.Ps.Stop() }; try { [void]$s.Ps.EndInvoke($s.H) } catch { }; $s.Ps.Dispose() }

$relT2 = T 'Updater+Release'
$trT = T 'Updater+Transfer'
function NewRel($port, [byte[]]$gzBody, [byte[]]$fullBody) {
    $r = [Activator]::CreateInstance($relT2)
    $r.Version = '9.9.9'
    $r.AppUrl = "http://127.0.0.1:$port/app.gz"; $r.AppSize = $gzBody.Length; $r.AppSha256 = Sha $gzBody
    $r.Url = "http://127.0.0.1:$port/Subtext.exe"; $r.Size = $fullBody.Length; $r.Sha256 = Sha $fullBody
    return $r
}
$fetchSlim = $updT.GetMethod('FetchSlim', $ST)
function TrySlim($rel, $selfPath, $out) {
    $tr = [Activator]::CreateInstance($trT)
    $a = New-Object object[] 4; $a[0] = $rel; $a[1] = [string]$selfPath; $a[2] = [string]$out; $a[3] = $tr
    return [string]$fetchSlim.Invoke($null, $a)
}

$routes = @{ '/app.gz' = $gz }
$s = Serve $routes 1 0
$out = Join-Path $work 'out.exe'
$e = TrySlim (NewRel $s.Port $gz $full) $self $out
Done $s
$okBytes = (Test-Path $out) -and ((Sha ([IO.File]::ReadAllBytes($out))) -eq (Sha $full))
Check 'T_SLIM_OK' ($e -eq '' -and $okBytes) ("err=" + $e + " same=" + $okBytes)

# מנוע אחר בגרסה החדשה: התוכנה החדשה + המנוע הישן ≠ מה שפורסם - אין קובץ, ונופלים להורדה המלאה
$fullOther = [byte[]]($appNew + (Pack (Rand 600000 99)) + $tail)
if (Test-Path $out) { Remove-Item $out }
$s = Serve $routes 1 0
$e = TrySlim (NewRel $s.Port $gz $fullOther) $self $out
Done $s
Check 'T_SLIM_ENGINE_CHANGED' ($e.Contains('לא זהה') -and -not (Test-Path $out)) $e

# קובץ דחוס שבור (והטביעה שלו נכונה - כאילו הועלה שבור)
$gzBad = [byte[]]$gz.Clone(); for ($i = 40; $i -lt 400; $i++) { $gzBad[$i] = 0x55 }
$s = Serve @{ '/app.gz' = $gzBad } 1 0
$e = TrySlim (NewRel $s.Port $gzBad $full) $self $out
Done $s
Check 'T_SLIM_BROKEN_GZ' ($e.Length -gt 0 -and -not (Test-Path $out)) $e

# העדכון הקטן נקטע באמצע / חסום בסינון - שגיאה, בלי קובץ
$s = Serve @{} 1 0
$e = TrySlim (NewRel $s.Port $gz $full) $self $out
Done $s
Check 'T_SLIM_404' ($e.Length -gt 0 -and -not (Test-Path $out)) $e

# מתי בכלל מנסים
$possible = $updT.GetMethod('SlimPossible', $ST)
function Possible($rel, $selfPath) { $a = New-Object object[] 3; $a[0] = $rel; $a[1] = [string]$selfPath; $r = $possible.Invoke($null, $a); return @{ Ok = [bool]$r; Why = [string]$a[2] } }
$r0 = NewRel 1 $gz $full
$p1 = Possible $r0 $self
$rNoApp = NewRel 1 $gz $full; $rNoApp.AppUrl = ''
$rNoSha = NewRel 1 $gz $full; $rNoSha.Sha256 = ''
Check 'T_SLIM_WHEN' ($p1.Ok -and -not (Possible $rNoApp $self).Ok -and -not (Possible $rNoSha $self).Ok -and -not (Possible $r0 $junk).Ok) ("ok=" + $p1.Ok + " / " + (Possible $rNoSha $self).Why + " / " + (Possible $r0 $junk).Why)

# ״ביטול״ באמצע הורדה איטית: חוזרים מיד, ובלי קובץ שלם
$fetchFile = $updT.GetMethod('FetchFile', $ST)
$big = Rand 3000000 21
$s = Serve @{ '/big.exe' = $big } 1 20
$tr = [Activator]::CreateInstance($trT)
$canceler = [powershell]::Create(); [void]$canceler.AddScript({ param($t) Start-Sleep -Milliseconds 700; $t.Abort() }).AddArgument($tr); $ch = $canceler.BeginInvoke()
$sw = [Diagnostics.Stopwatch]::StartNew()
$a = New-Object object[] 6; $a[0] = [string]"http://127.0.0.1:$($s.Port)/big.exe"; $a[1] = [string](Join-Path $work 'big.exe'); $a[2] = [long]$big.Length; $a[3] = [string]''; $a[4] = [long]1000; $a[5] = $tr
$e = [string]$fetchFile.Invoke($null, $a)
$took = $sw.Elapsed.TotalSeconds
try { [void]$canceler.EndInvoke($ch) } catch { }; $canceler.Dispose()
$s.Ps.Stop(); $s.Ps.Dispose()
Check 'T_CANCEL' ($e.Contains('בוטלה') -and $took -lt 5 -and $tr.Got -lt $big.Length) ("{0:0.0}s got={1} err={2}" -f $took, $tr.Got, $e)

# ״אפליקציות מותקנות״ אחרי עדכון קטן: הגרסה החדשה מעדכנת את הרשומה שלה - ורק אותה
$appVer = [string](T 'App').GetField('Version', $ST).GetValue($null)
$idir = Join-Path $work 'installed'; New-Item -ItemType Directory -Force $idir | Out-Null
$iexe = Join-Path $idir 'Subtext.exe'; WriteFile $iexe (Rand 200000 31)
[IO.File]::WriteAllText((Join-Path $idir 'installed.txt'), "installed=1`r`nversion=0.0.1`r`ndate=2026-01-01 10:00`r`ndir=$idir`r`nsize=5`r`nchannel=setup`r`n", [Text.Encoding]::UTF8)
$kU = 'Software\SubtextTest\Uninstall'; $kA = 'Software\SubtextTest\App'
foreach ($k in $kU, $kA) { $rk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($k); $rk.SetValue('InstallLocation', $idir); $rk.SetValue('DisplayVersion', '0.0.1'); $rk.SetValue('Version', '0.0.1'); $rk.Close() }
$refresh = (T 'Install').GetMethod('Refresh', $ST, $null, [Type[]]@([string], [string], [string]), $null)
$c1 = $refresh.Invoke($null, @([string]$iexe, $kU, $kA))
$marker = [IO.File]::ReadAllText((Join-Path $idir 'installed.txt'))
$rk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($kU); $dv = $rk.GetValue('DisplayVersion'); $rk.Close()
$rk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($kA); $av = $rk.GetValue('Version'); $rk.Close()
$c2 = $refresh.Invoke($null, @([string]$iexe, $kU, $kA))
Check 'T_INSTALL_REFRESH' ($c1 -and -not $c2 -and $marker.Contains("version=$appVer") -and $marker.Contains('size=200000') -and $dv -eq $appVer -and $av -eq $appVer) ("first=$c1 second=$c2 reg=$dv/$av")
# רשומה של התקנה אחרת (תיקייה אחרת) - לא נוגעים
$rk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($kU); $rk.SetValue('InstallLocation', 'C:\Elsewhere'); $rk.SetValue('DisplayVersion', '0.0.2'); $rk.Close()
[void]$refresh.Invoke($null, @([string]$iexe, $kU, $kA))
$rk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($kU); $dv2 = $rk.GetValue('DisplayVersion'); $rk.Close()
Check 'T_INSTALL_OTHER' ($dv2 -eq '0.0.2') $dv2
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree('Software\SubtextTest', $false)

# תשובת גיטהאב: הקובץ הקטן נקרא לחוד, ולא נלקח כ״קובץ הנייד״ גם כשהוא ראשון ברשימה
$withApp = Release @((Asset 'Subtext-app-update.gz' 480000 'BeniaBot'), (Asset 'Subtext-Setup.exe' 41000000 'BeniaBot'),
                     (Asset 'Subtext.exe' 38000000 'BeniaBot')) '0.9.9' 'x'
$p = DoParse $withApp
Check 'T_PARSE_APP' ((F $p.Rel 'AppUrl') -like '*/Subtext-app-update.gz' -and (F $p.Rel 'AppSize') -eq 480000 -and (F $p.Rel 'Url') -like '*/Subtext.exe') ((F $p.Rel 'AppUrl') + ' | ' + (F $p.Rel 'Url'))
$noApp = Release @((Asset 'Subtext.exe' 38000000 'BeniaBot')) '0.9.9' 'x'
Check 'T_PARSE_NOAPP' ((F (DoParse $noApp).Rel 'AppUrl') -eq '') ''

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

# ================= המנוע בסוף הקובץ, דחוס ב-LZMA (0.8.8) =================
Write-Host 'HDR_ENGINE'
function Pack2 { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $v = $args[$i]; if ($null -ne $v) { $v = $v.psobject.BaseObject }; $a[$i] = $v }; return ,$a }
$work2 = Join-Path $env:TEMP ('ss-eng-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Force $work2 | Out-Null
$unpack = $rtT.GetMethod('Unpack', $ST)
$ffSrc = Join-Path $root 'tools\ffmpeg.exe'

# הקובץ האמיתי: פריסה, וטביעה זהה ל-ffmpeg.exe שנארז
$dest = Join-Path $work2 'ffmpeg.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
$err = ''
try { [void]$unpack.Invoke($null, (Pack2 ([string]$exe) ([string]$dest) $null)) } catch { $err = $_.Exception.InnerException.Message }
$took = $sw.Elapsed.TotalSeconds
$same = (Test-Path $dest) -and (Test-Path $ffSrc) -and ((Get-FileHash $dest).Hash -eq (Get-FileHash $ffSrc).Hash)
Check 'T_ENGINE_UNPACK' ($err -eq '' -and $same -and $took -lt 20) ("{0:0.0}s same={1} {2}" -f $took, $same, $err)

# הגודל והזהות, דרך SUBSTUDIO_EXE (בבדיקות אין לאסמבלי מיקום)
$env:SUBSTUDIO_EXE = $exe
$psz = [long]$rtT.GetProperty('PayloadSize', $ST).GetValue($null, $null)
$eid = [string]$rtT.GetProperty('EngineId', $ST).GetValue($null, $null)
Check 'T_ENGINE_IDENTITY' ($psz -eq (Get-Item $ffSrc).Length -and $eid -eq $td.Id) ("size=$psz id=" + $eid.Substring(0, [Math]::Min(12, $eid.Length)))

# קובץ תוכנה שנפגם באמצע המנוע: הודעה ברורה, ושום מנוע לא נכתב
$dmg = Join-Path $work2 'damaged.exe'
Copy-Item $exe $dmg
$fs = [IO.File]::Open($dmg, 'Open', 'ReadWrite')
$fs.Seek([long]($td.R[0] + 5000000), 'Begin') | Out-Null
$junkBytes = Rand 4096 41; $fs.Write($junkBytes, 0, $junkBytes.Length); $fs.Dispose()
$dest2 = Join-Path $work2 'ffmpeg2.exe'
$err = ''
try { [void]$unpack.Invoke($null, (Pack2 ([string]$dmg) ([string]$dest2) $null)) } catch { $err = $_.Exception.InnerException.Message }
Check 'T_ENGINE_DAMAGED' ($err.Contains('פגום') -and -not (Test-Path $dest2)) $err

# זרם LZMA שנקטע: שגיאה מיד, לא תקיעה ולא קובץ ענק
$lzT = T 'LzmaDecoder'
$all = [IO.File]::ReadAllBytes($exe)
$start = [int]$td.R[0] + 12
$part = New-Object byte[] 2000000; [Array]::Copy($all, $start, $part, 0, $part.Length)
$ms = New-Object IO.MemoryStream (, $part)
$outMs = New-Object IO.MemoryStream
$sw = [Diagnostics.Stopwatch]::StartNew(); $err = ''
try { [void]$lzT.GetMethod('Decode').Invoke([Activator]::CreateInstance($lzT), (Pack2 $ms $outMs ([long](Get-Item $ffSrc).Length) $null)) } catch { $err = $_.Exception.InnerException.Message }
Check 'T_LZMA_TRUNCATED' ($err.Length -gt 0 -and $sw.Elapsed.TotalSeconds -lt 10 -and $outMs.Length -lt 20000000) ("{0:0.0}s out={1} err={2}" -f $sw.Elapsed.TotalSeconds, $outMs.Length, $err)

Remove-Item $work2 -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
