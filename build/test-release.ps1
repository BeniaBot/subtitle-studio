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

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
