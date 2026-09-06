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
$exe = Join-Path $root 'dist\SubtitleStudio.exe'
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
    return '{"url":"https://api.github.com/repos/o/r/releases/assets/1","id":1,' +
           '"node_id":"RA_kwDO","name":"' + $name + '","label":null,' +
           '"uploader":' + (Uploader $login) + ',' +
           '"content_type":"application/x-msdownload","state":"uploaded",' +
           '"size":' + $size + ',"download_count":7,' +
           '"created_at":"2026-09-01T10:00:00Z","updated_at":"2026-09-01T10:01:00Z",' +
           '"browser_download_url":"https://github.com/o/r/releases/download/v9.9.9/' + $name + '"}'
}
function Release($assets, $tag, $body) {
    return '{"url":"https://api.github.com/repos/o/r/releases/1","tag_name":"' + $tag + '",' +
           '"name":"' + $tag + '","draft":false,"prerelease":false,' +
           '"body":"' + $body + '","assets":[' + ($assets -join ',') + ']}'
}

Write-Host 'HDR_FIXED'

$both = Release @((Asset 'SubtitleStudio-Setup.exe' 41000000 'BeniaBot'),
                  (Asset 'SubtitleStudio.exe' 38000000 'BeniaBot')) '0.9.9' 'notes here'
$p = DoParse $both
Check 'T_BOTH_NOERR' ($null -eq $p.Err -and $null -ne $p.Rel) ("err=" + $p.Err)
Check 'T_BOTH_VER'   ((F $p.Rel 'Version') -eq '0.9.9') (F $p.Rel 'Version')
Check 'T_BOTH_PORT'  ((F $p.Rel 'Url') -like '*/SubtitleStudio.exe') (F $p.Rel 'Url')
Check 'T_BOTH_SETUP' ((F $p.Rel 'SetupUrl') -like '*/SubtitleStudio-Setup.exe') (F $p.Rel 'SetupUrl')
Check 'T_BOTH_SZ1'   ((F $p.Rel 'Size') -eq 38000000) (F $p.Rel 'Size')
Check 'T_BOTH_SZ2'   ((F $p.Rel 'SetupSize') -eq 41000000) (F $p.Rel 'SetupSize')

$long = Release @((Asset 'SubtitleStudio-Setup.exe' 41000000 ('a' * 120)),
                  (Asset 'SubtitleStudio.exe' 38000000 ('a' * 120))) '1.0.0' 'x'
$p = DoParse $long
Check 'T_LONGUPLOADER' ((F $p.Rel 'Url').Length -gt 0 -and (F $p.Rel 'SetupUrl').Length -gt 0) ("url=" + (F $p.Rel 'Url').Length + " setup=" + (F $p.Rel 'SetupUrl').Length)

$onlySetup = Release @((Asset 'SubtitleStudio-Setup.exe' 41000000 'BeniaBot')) '1.0.1' 'x'
$p = DoParse $onlySetup
Check 'T_ONLYSETUP_HAS' ((F $p.Rel 'SetupUrl').Length -gt 0) (F $p.Rel 'SetupUrl')
Check 'T_ONLYSETUP_NOPORT' ((F $p.Rel 'Url') -eq '') ("'" + (F $p.Rel 'Url') + "'")

$onlyPort = Release @((Asset 'SubtitleStudio.exe' 38000000 'BeniaBot')) '1.0.2' 'x'
$p = DoParse $onlyPort
Check 'T_ONLYPORT_HAS' ((F $p.Rel 'Url').Length -gt 0) (F $p.Rel 'Url')
Check 'T_ONLYPORT_NOSETUP' ((F $p.Rel 'SetupUrl') -eq '') ("'" + (F $p.Rel 'SetupUrl') + "'")

$noise = Release @((Asset 'SHA256SUMS.txt' 200 'BeniaBot'),
                   (Asset 'source.zip' 900 'BeniaBot'),
                   (Asset 'SubtitleStudio.exe' 38000000 'BeniaBot')) '1.0.3' 'x'
$p = DoParse $noise
Check 'T_NOISE' ((F $p.Rel 'Url') -like '*/SubtitleStudio.exe') (F $p.Rel 'Url')

$two = Release @((Asset 'SubtitleStudio-Setup.exe' 41000000 'BeniaBot'),
                 (Asset 'SubtitleStudio-Setup-x86.exe' 39000000 'BeniaBot')) '1.0.4' 'x'
$p = DoParse $two
Check 'T_FIRSTSETUPWINS' ((F $p.Rel 'SetupUrl') -like '*/SubtitleStudio-Setup.exe') (F $p.Rel 'SetupUrl')

$evil = (Release @((Asset 'SubtitleStudio.exe' 38000000 'BeniaBot')) '1.0.5' 'x').Replace('https://github.com/o/r/releases/download/v9.9.9/', 'https://evil.example.com/')
$p = DoParse $evil
Check 'T_FOREIGNURL' ((F $p.Rel 'Url') -eq '') ("'" + (F $p.Rel 'Url') + "'")

$esc = Release @((Asset 'SubtitleStudio.exe' 1 'BeniaBot')) '1.0.6' 'line one\nline two \"quoted\" and a backslash \\\\ end'
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
    $wc.Headers.Add('User-Agent', 'SubtitleStudio-test')
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
} catch {
    Write-Host ("  --   HDR_NONET: " + $_.Exception.Message)
}

Write-Host ''
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
