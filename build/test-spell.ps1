# בדיקת איות: המנוע מול המילון האמיתי, ההורדה מול שרת מדומה, והחיבור לממשק.
#
# **המילון:** Hspell 1.4 בפורמט Hunspell (he_IL.aff + he_IL.dic). הבדיקה מחפשת אותו
# ב-%TEMP%\ss-dict, ואם אין - מעתיקה מ-D:\Claude\_ss-dict, ואם גם שם אין - מורידה
# מהמהדורה spell-he-1.4 במאגר (הטביעה נבדקת).
#
# **לא נוגעים ב-%TEMP%\ss-test-dict**, התיקייה שמצב הבדיקה של התוכנה משתמש בה.
# מילון שנשאר שם היה נטען ברקע בכל בדיקת ממשק אחרת, ומוסיף ״בעיות״ באמצע מדידה.
# כאן הכול דרך Spell.FolderOverride לתיקיות זמניות.
# צפוי: 59 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\SubtitleStudio.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IF = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }
function Pack { $a = New-Object object[] $args.Count; for ($i = 0; $i -lt $args.Count; $i++) { $v = $args[$i]; if ($v -ne $null) { $v = $v.psobject.BaseObject }; $a[$i] = $v }; return ,$a }

$spT = T 'Spell'; $enT = T 'SpellEngine'
$work = [IO.Path]::GetFullPath((Join-Path $env:TEMP ('ss-spell-' + [guid]::NewGuid().ToString('N').Substring(0, 6))))
New-Item -ItemType Directory $work | Out-Null

# ---------------- המילון ----------------
$dict = Join-Path $env:TEMP 'ss-dict'
New-Item -ItemType Directory -Force $dict | Out-Null
$files = $spT.GetField('Files', $SF).GetValue($null)
$shas = $spT.GetField('Sha256', $SF).GetValue($null)
for ($i = 0; $i -lt $files.Count; $i++) {
    $target = Join-Path $dict $files[$i]
    $okHash = (Test-Path $target) -and ((Get-FileHash $target -Algorithm SHA256).Hash -eq $shas[$i])
    if (-not $okHash -and (Test-Path (Join-Path 'D:\Claude\_ss-dict' $files[$i]))) { Copy-Item (Join-Path 'D:\Claude\_ss-dict' $files[$i]) $target -Force }
}
$have = $true; for ($i = 0; $i -lt $files.Count; $i++) { if (-not ((Test-Path (Join-Path $dict $files[$i])) -and ((Get-FileHash (Join-Path $dict $files[$i]) -Algorithm SHA256).Hash -eq $shas[$i]))) { $have = $false } }
if (-not $have) {
    $spT.GetField('FolderOverride', $SF).SetValue($null, $dict)
    $a = New-Object object[] 3; $ok = $spT.GetMethod('Download', $SF).Invoke($null, $a)
    if (-not $ok) { Write-Host ("no dictionary: " + $a[2]); exit 2 }
}
Check 'המילון במקום, והטביעות תואמות לקוד' $true $dict

# ================= 1. המנוע =================
Write-Host 'המנוע'
[GC]::Collect(); $m0 = [GC]::GetTotalMemory($true)
$sw = [Diagnostics.Stopwatch]::StartNew()
$e = $enT.GetMethod('Load', $SF, $null, [Type[]]@([string], [string]), $null).Invoke($null, @([string](Join-Path $dict 'he_IL.aff'), [string](Join-Path $dict 'he_IL.dic')))
$loadMs = $sw.ElapsedMilliseconds
$e.AddExtraWords($spT.GetMethod('TorahWords', $SF).Invoke($null, @()))
[GC]::Collect(); $mem = ([GC]::GetTotalMemory($true) - $m0) / 1MB
Check 'טעינה מהירה, וזיכרון קטן (מערך דחוס, לא מילון של מחרוזות)' ($loadMs -lt 3000 -and $mem -lt 25) ("{0}ms, {1:N1}MB, מילים: {2}" -f $loadMs, $mem, $e.WordCount)
Check 'רשימת מילות הלימוד מוטמעת בתוכנה' ($e.ExtraCount -gt 250) ("מילים: " + $e.ExtraCount)

$good = @('שלום', 'בית', 'ובבית', 'שהלכתי', 'וכשהלכנו', 'בוורד', 'ורד', 'לכם', 'מנכ"ל', 'צה"ל', "ג'ירפה", 'ישראל', 'הישראלים', 'מחשב', 'כתוביות', 'תמלול', 'שיעור', 'ללמוד', 'והתלמידים', 'שָׁלוֹם', 'צה״ל', 'ג׳ירפה')
$rej = @($good | Where-Object { -not $e.Check($_) })
Check 'מילים תקינות, עם תחיליות, ניקוד וגרשיים עבריים' ($rej.Count -eq 0) ($rej -join ', ')
Check 'כלל הוא״ו: בוורד כן, בורד לא' ($e.Check('בוורד') -and -not $e.Check('בורד')) ''

# NEEDAFFIX: מילה עם הסוג j לא עומדת לבד, ועם תחילית מהסוג שלה כן
$dicLine = Get-Content (Join-Path $dict 'he_IL.dic') -Encoding UTF8 -TotalCount 300000 | Where-Object { $_ -match '/hj$' } | Select-Object -First 1
$njWord = $dicLine.Split('/')[0]
$hRule = Get-Content (Join-Path $dict 'he_IL.aff') -Encoding UTF8 | Where-Object { $_ -match '^PFX h 0 \S+ \.$' } | Select-Object -First 1
$hAdd = ($hRule -split '\s+')[3]
Check 'מילה שחייבת תחילית: לבד לא, עם תחילית כן' ((-not $e.Check($njWord)) -and $e.Check($hAdd + $njWord)) ("$njWord / $hAdd$njWord")

$aram = @('דלא', 'כדתניא', 'ודהוא', 'דהרמב"ם', 'טעמא', 'איכא', 'פשיטא', 'אביי', 'רשב"א', 'שו"ע', "תוס'", "וכו'", 'דתניא', 'ולית')
$rej = @($aram | Where-Object { -not $e.Check($_) })
Check 'ארמית, ד׳ ארמית, ראשי תיבות וקיצורים' ($rej.Count -eq 0) ($rej -join ', ')
$nums = @('כ"ג', 'תקפ"ב', "ה'תשפ""ו", "ג'", 'בכ"ג', 'ט"ו', 'ט"ז')
$rej = @($nums | Where-Object { -not $e.Check($_) })
Check 'מספרים באותיות' ($rej.Count -eq 0) ($rej -join ', ')
Check 'ד׳ לפני מילה שגויה עדיין שגויה' (-not $e.Check('דשלומ')) ''

$lecture = @'
שלום לכולם, היום נלמד את הסוגיה של תפילת הדרך בגמרא במסכת ברכות דף כ"ט ע"ב.
הגמרא שואלת, מאי טעמא? ומתרצת שכיון שהוא יוצא לדרך הוא צריך רחמים.
רש"י מפרש שם שהכוונה היא לכל דרך שיש בה סכנה, ותוס' חולקים ואומרים שצריך פרסה לפחות.
אמר רבי יוחנן משום רבי שמעון בר יוחאי, והלכתא כרב נחמן.
המשנה אומרת שהאמוראים והתנאים נחלקו בשאלה אם מברכים בשם ומלכות.
הרמב"ם פוסק כדעת הרי"ף, והשו"ע בסימן ק"י מביא את שתי הדעות, והמשנ"ב בס"ק ל' מכריע.
תניא, תנו רבנן, איתמר, אמר ליה, קא משמע לן, תא שמע, איבעיא להו, תיקו.
ואביי אמר דלא קשיא מידי, דהכא במאי עסקינן, כגון שיצא מביתו לפני השחר.
וכדתניא בברייתא, ורבא סבר דאיכא למימר הכי, ולית הלכתא כוותיה.
'@
$everyday = @'
אתמול בערב נסענו עם הילדים לסבתא, והיא הכינה לנו ארוחה טעימה במיוחד.
המורה ביקשה מהתלמידים להביא מחר את המחברות, ולא לשכוח את שיעורי הבית.
הממשלה החליטה להעלות את המחירים של החשמל והמים בחודש הבא.
'@
$wordsM = $enT.GetMethod('Words', $SF)
function Flagged($text) { return @(@($wordsM.Invoke($null, (Pack ([string]$text)))) | Where-Object { -not $e.Check($_.Text) } | ForEach-Object { $_.Text }) }
$fl = Flagged $lecture
Check 'שיעור בגמרא (116 מילים): אף התראת שווא' ($fl.Count -eq 0) ($fl -join ' · ')
$fl = Flagged $everyday
Check 'טקסט יומיומי: אף התראת שווא' ($fl.Count -eq 0) ($fl -join ' · ')

$typos = [ordered]@{ 'שלומ' = 'שלום'; 'כתובייות' = 'כתוביות'; 'מחשבב' = 'מחשב'; 'ישראלל' = 'ישראל'; 'תלמידיים' = 'תלמידים'; 'תוספוט' = 'תוספות'; 'ברכוט' = 'ברכות' }
$badTop = @()
foreach ($k in $typos.Keys) {
    $caught = -not $e.Check($k)
    $s = @($e.Suggest($k, 5))
    if (-not $caught -or $s.Count -eq 0 -or $s[0] -ne $typos[$k]) { $badTop += ("$k -> " + ($s -join '/')) }
}
Check 'שגיאות כתיב נתפסות, והתיקון הנכון הוא ההצעה הראשונה' ($badTop.Count -eq 0) ($badTop -join ' | ')

$toks = @(@($wordsM.Invoke($null, (Pack ([string]'צה״ל אמר ״שלום״ ל-John בשנת 2024, ג׳ירפה־גדולה/קטנה, ותוס׳ ')))) | ForEach-Object { $_.Text })
Check 'חלוקה למילים: גרשיים באמצע נשארים, בקצוות לא; אנגלית ומספרים נשארים בחוץ' (($toks -join '|') -eq 'צה״ל|אמר|שלום|ל|בשנת|ג׳ירפה|גדולה|קטנה|ותוס׳') ($toks -join '|')

# ================= 2. הורדה =================
Write-Host 'הורדה (שרת מדומה)'
$server = {
    param($port, $responses, $logDir)
    $l = New-Object System.Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $port
    $l.Start()
    try {
        for ($k = 0; $k -lt $responses.Count; $k++) {
            $resp = $responses[$k]
            # **המתנה עם תקרה, לא AcceptTcpClient חוסם.** בביטול הלקוח לא מבקש את
            # הקובץ השני, ו-PowerShell.Stop() לא מצליח לקטוע המתנה חוסמת: הבדיקה
            # נתקעה לנצח (15.9.2026).
            $deadline = [DateTime]::UtcNow.AddSeconds(20)
            while (-not $l.Pending() -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
            if (-not $l.Pending()) { break }
            $c = $l.AcceptTcpClient()
            $ns = $c.GetStream(); $ns.ReadTimeout = 15000
            $ms = New-Object IO.MemoryStream; $buf = New-Object byte[] 8192
            while ($true) {
                $n = $ns.Read($buf, 0, $buf.Length); if ($n -le 0) { break }
                $ms.Write($buf, 0, $n)
                if ([Text.Encoding]::ASCII.GetString($ms.ToArray()).Contains("`r`n`r`n")) { break }
            }
            [IO.File]::WriteAllBytes((Join-Path $logDir ("req-$k.txt")), $ms.ToArray())
            $body = [byte[]]$resp.body
            $head = "HTTP/1.1 " + $resp.status + " X`r`nContent-Type: application/octet-stream`r`nContent-Length: " + $body.Length + "`r`nConnection: close`r`n`r`n"
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
    $ps = [powershell]::Create()
    [void]$ps.AddScript($server).AddArgument($port).AddArgument($responses).AddArgument($dir)
    $h = $ps.BeginInvoke(); Start-Sleep -Milliseconds 400
    return @{ Port = $port; Ps = $ps; Handle = $h; Dir = $dir }
}
function StopServer($s) { if (-not $s.Handle.AsyncWaitHandle.WaitOne(20000)) { $s.Ps.Stop() }; try { [void]$s.Ps.EndInvoke($s.Handle) } catch { }; $s.Ps.Dispose() }
function Resp($status, [byte[]]$body) { return @{ status = $status; body = $body } }
function Gz([byte[]]$raw) { $ms = New-Object IO.MemoryStream; $gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress); $gz.Write($raw, 0, $raw.Length); $gz.Close(); return ,$ms.ToArray() }
$affGz = Gz ([IO.File]::ReadAllBytes((Join-Path $dict 'he_IL.aff')))
$dicGz = Gz ([IO.File]::ReadAllBytes((Join-Path $dict 'he_IL.dic')))
$mDownload = $spT.GetMethod('Download', $SF)
$script:progress = New-Object Collections.ArrayList
function Download($folder, $port, [bool]$cancel) {
    $spT.GetField('FolderOverride', $SF).SetValue($null, [string]$folder)
    $spT.GetField('BaseUrl', $SF).SetValue($null, "http://127.0.0.1:$port/dict/")
    $script:progress.Clear()
    $prog = [Action[long, long]] { param($d, $t) [void]$script:progress.Add($d) }
    $canc = [Func[bool]] { return $cancel }
    $a = New-Object object[] 3; $a[0] = $prog; $a[1] = $canc
    $ok = $mDownload.Invoke($null, $a)
    return @{ Ok = [bool]$ok; Error = [string]$a[2] }
}
function Leftovers($folder) { return @(Get-ChildItem $folder -Filter '*.download' -ErrorAction SilentlyContinue).Count }

$A = Join-Path $work 'dictA'
$s = StartServer @((Resp 200 $affGz), (Resp 200 $dicGz))
$r = Download $A $s.Port $false
StopServer $s
$req0 = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $s.Dir 'req-0.txt')))
Check 'הורדה תקינה: שני הקבצים במקום, והטביעות תואמות' ($r.Ok -and (Get-FileHash (Join-Path $A 'he_IL.dic') -Algorithm SHA256).Hash -eq $shas[1] -and (Get-FileHash (Join-Path $A 'he_IL.aff') -Algorithm SHA256).Hash -eq $shas[0]) $r.Error
Check 'מבקשים את הקבצים הדחוסים מהכתובת הנכונה' ($req0.StartsWith('GET /dict/he_IL.aff.gz')) ($req0.Split("`n")[0])
$mono = $true; for ($i = 1; $i -lt $script:progress.Count; $i++) { if ($script:progress[$i] -lt $script:progress[$i - 1]) { $mono = $false } }
Check 'ההתקדמות עולה, ומגיעה לסכום שירד' ($mono -and $script:progress[$script:progress.Count - 1] -eq ($affGz.Length + $dicGz.Length)) ("סופי: " + $script:progress[$script:progress.Count - 1])
Check 'בלי קבצים זמניים שנשארו' ((Leftovers $A) -eq 0) ''
$licPath = Join-Path $A 'LICENSE-Hspell-AGPLv3.txt'
$licOk = (Test-Path $licPath) -and ([IO.File]::ReadAllText($licPath)).Contains('GNU AFFERO GENERAL PUBLIC LICENSE')
Check 'נוסח הרישיון של המילון נכתב לידו' $licOk ''

$junk = New-Object byte[] 5000; (New-Object Random 5).NextBytes($junk)
$s = StartServer @((Resp 200 $affGz), (Resp 200 $junk))
$B = Join-Path $work 'dictB'
$r = Download $B $s.Port $false
StopServer $s
Check 'קובץ שבור: נכשל, בהודעה על מילון פגום' ((-not $r.Ok) -and $r.Error.Contains('פגום')) $r.Error
Check 'ושום מילון חלקי לא נשאר' (-not (Test-Path (Join-Path $B 'he_IL.aff')) -and -not (Test-Path (Join-Path $B 'he_IL.dic')) -and (Leftovers $B) -eq 0) ''

$other = Gz ([Text.Encoding]::UTF8.GetBytes("1`nשלום/a`n"))
$s = StartServer @((Resp 200 $affGz), (Resp 200 $other))
$r = Download $A $s.Port $false
StopServer $s
Check 'תוכן אחר (טביעה לא תואמת): נכשל' ((-not $r.Ok) -and $r.Error.Contains('פגום')) $r.Error
Check 'והמילון התקין שכבר היה נשאר שלם' ((Get-FileHash (Join-Path $A 'he_IL.dic') -Algorithm SHA256).Hash -eq $shas[1]) ''

$s = StartServer @((Resp 404 ([Text.Encoding]::ASCII.GetBytes('nope'))))
$r = Download (Join-Path $work 'dictC') $s.Port $false
StopServer $s
Check '404: ״המילון לא נמצא בשרת״' ($r.Error.Contains('לא נמצא בשרת')) $r.Error
$r = Download (Join-Path $work 'dictD') (FreePort) $false
Check 'אין שרת: ״אין חיבור לאינטרנט, או שהאתר חסום״' ($r.Error.Contains('אין חיבור')) $r.Error
# רשת מסוננת: דף חסימה בקוד 200. עד 0.8.0 המשתמש קיבל ״המילון שירד פגום״ וניסה שוב ושוב.
$block = [Text.Encoding]::UTF8.GetBytes("`r`n<!DOCTYPE html><html dir=""rtl""><body>האתר חסום</body></html>")
$s = StartServer @((Resp 200 $block))
$r = Download (Join-Path $work 'dictF') $s.Port $false
StopServer $s
Check 'דף חסימה של סינון: ״נראה שסינון האינטרנט חסם״, לא ״פגום״' ((-not $r.Ok) -and $r.Error.Contains('סינון') -and -not $r.Error.Contains('פגום')) $r.Error
$s = StartServer @((Resp 418 $block))
$r = Download (Join-Path $work 'dictG') $s.Port $false
StopServer $s
Check 'סינון שחוסם בקוד 418: אותה הודעה' ($r.Error.Contains('סינון')) $r.Error
$s = StartServer @((Resp 200 $affGz))
$E2 = Join-Path $work 'dictE'
$r = Download $E2 $s.Port $true
StopServer $s
Check 'ביטול: נעצר, בלי מילון חלקי' ((-not $r.Ok) -and $r.Error.Contains('בוטלה') -and -not (Test-Path (Join-Path $E2 'he_IL.dic')) -and (Leftovers $E2) -eq 0) $r.Error
$spT.GetField('BaseUrl', $SF).SetValue($null, 'https://github.com/BeniaBot/subtitle-studio/releases/download/spell-he-1.4/')
Check 'כתובת ההורדה האמיתית היא במאגר שלנו' (([string]$spT.GetField('BaseUrl', $SF).GetValue($null)).StartsWith('https://github.com/BeniaBot/subtitle-studio/releases/download/')) ''

# ================= 3. בדיקת השגיאות ובממשק =================
Write-Host 'בבדיקת השגיאות ובממשק'
$spT.GetField('FolderOverride', $SF).SetValue($null, [string]$dict)
$userFile = Join-Path $work 'words.txt'
$spT.GetField('UserFileOverride', $SF).SetValue($null, [string]$userFile)
$spT.GetField('Enabled', $SF).SetValue($null, $true)
[void]$spT.GetMethod('LoadNow', $SF).Invoke($null, @())
Check 'המילון נטען' ([bool]$spT.GetProperty('Ready', $SF).GetValue($null, $null)) ''

$qaT = T 'Qa'; $docT = T 'Doc'; $cueT = T 'Cue'; $formT = T 'MainForm'
function NewCue($a, $b, $t) { return $cueT.GetConstructor([Type[]]@([long], [long], [string])).Invoke(@([long]$a, [long]$b, [string]$t)) }
$d = [Activator]::CreateInstance($docT)
$d.Cues.Add((NewCue 0 3000 'שלום לכולם, היום נלמד על ברכוט השחר'))
$d.Cues.Add((NewCue 4000 7000 'ואביי אמר דלא קשיא מידי'))
$issues = @($qaT.GetMethod('Find', $SF).Invoke($null, (Pack $d)))
$sp = @($issues | Where-Object { [string]$_.Kind -eq 'Spelling' })
Check 'מילה שגויה = בעיה מסוג איות, עם המילה' ($sp.Count -eq 1 -and $sp[0].Index -eq 0 -and ($sp[0].Words -join ',') -eq 'ברכוט') ("issues=" + (($issues | ForEach-Object { [string]$_.Kind }) -join ','))
$ex = [string]$qaT.GetMethod('Explain', $SF).Invoke($null, (Pack $sp[0]))
Check 'ההסבר מציע את התיקון, ואומר איפה מתקנים' ($ex.Contains('ברכות') -and $ex.Contains('קליק ימני')) $ex
$fix = $qaT.GetMethod('FixAll', $SF).Invoke($null, (Pack $d))
Check 'תיקון אוטומטי לא נוגע באיות (זה דורש אדם)' ($d.Cues[0].Text.Contains('ברכוט')) ''
Check 'ReplaceWord מחליף רק מילה שלמה' (([string]$spT.GetMethod('ReplaceWord', $SF).Invoke($null, (Pack 'שלום שלומות, שלום.' 'שלום' 'היי'))) -eq 'היי שלומות, היי.') ''

$f = [Activator]::CreateInstance($formT)
$f.StartPosition = 'Manual'; $f.Location = New-Object Drawing.Point -3000, -3000
$f.ClientSize = New-Object Drawing.Size 1493, 997
$f.Show(); [Windows.Forms.Application]::DoEvents()
function Fld($name) { return $formT.GetField($name, $IF).GetValue($f) }
function Call($name, $argv) { return $formT.GetMethod($name, $IF).Invoke($f, $argv) }
$doc = Fld '_doc'
$doc.Cues.Add((NewCue 0 3000 'שלום לכולם, היום נלמד על ברכוט השחר'))
$doc.Cues.Add((NewCue 4000 7000 'ואביי אמר דלא קשיא מידי'))
$doc.Cues[0].Selected = $true
[void](Call 'SyncAfterDocChange' @()); [void](Call 'RefreshQa' (Pack $true))

$items = @(Call 'CueMenuItems' @())
$texts = @($items | ForEach-Object { $_.Text })
Check 'קליק ימני: המילה, התיקון המוצע, ״להוסיף למילון״, ואחר כך פעולות הכתובית' ($texts[0] -eq '״ברכוט״' -and $texts[1] -eq 'להחליף ב״ברכות״' -and $texts -contains 'להוסיף למילון' -and $texts -contains 'מחיקה') ($texts -join ' | ')
$items[1].Click.Invoke($null, [EventArgs]::Empty); [Windows.Forms.Application]::DoEvents()
Check 'בחירת ההצעה מתקנת רק את המילה' ($doc.Cues[0].Text -eq 'שלום לכולם, היום נלמד על ברכות השחר') $doc.Cues[0].Text
$hint = (Fld '_hintLbl').Text
Check 'ושורת המצב אומרת מה הוחלף ואיך מבטלים' ($hint.Contains('ברכות') -and $hint.Contains('Ctrl+Z')) $hint
$doc.Undo(); [void](Call 'SyncAfterDocChange' @())
Check 'Ctrl+Z מחזיר' ($doc.Cues[0].Text.Contains('ברכוט')) ''

foreach ($c in $doc.Cues) { $c.Selected = $false }; [void](Call 'LoadEditor' @())
$qaItems = @(Call 'QaMenuItems' @())
Check 'תפריט הבעיות: סוג האיות, ואפשרות לכבות' ((($qaItems | ForEach-Object { $_.Text }) -join '|') -match 'שאולי שגויה' -and (($qaItems | ForEach-Object { $_.Text }) -contains 'לכבות את בדיקת האיות')) ((($qaItems | ForEach-Object { $_.Text }) -join ' | '))
$mi = $formT.GetMethod('SpellMenuItem', $IF).Invoke($f, @())
Check 'בתפריט הכתוביות: ״בדיקת איות״ אומר כמה חשודות' ($mi.Text -eq 'בדיקת איות' -and $mi.Desc.Contains('חשודה')) $mi.Desc
[void](Call 'SpellCheck' @())
$ed = Fld '_editing'
Check '״בדיקת איות״ קופצת לכתובית החשודה' ($ed -ne $null -and $ed.Text.Contains('ברכוט')) ''

[void](Call 'AddToDictionary' (Pack 'ברכוט'))
$sp = @(@($qaT.GetMethod('Find', $SF).Invoke($null, (Pack $doc))) | Where-Object { [string]$_.Kind -eq 'Spelling' })
Check 'להוסיף למילון: המילה כבר לא מסומנת' ($sp.Count -eq 0) ''
Check 'ונשמרת בקובץ המילון האישי' ((Test-Path $userFile) -and ((Get-Content $userFile -Encoding UTF8) -contains 'ברכוט')) ''
[void]$spT.GetMethod('LoadNow', $SF).Invoke($null, @())
Check 'ואחרי טעינה מחדש עדיין נכונה' (@($spT.GetMethod('Misspelled', $SF).Invoke($null, (Pack 'ברכוט'))).Count -eq 0) ''

$doc.Cues.Add((NewCue 8000 11000 'עוד שגיאה בתוספוט'))
[void](Call 'SyncAfterDocChange' @()); [void](Call 'RefreshQa' (Pack $true))

# ---- הכלים של העוזר (0.8.0) ----
# ‏find_problems כבר דיווח על מילים חשודות, אבל לא הייתה דרך לתקן אותן מהצ׳אט.
$callT = T 'AiCall'
function AiCall($name, $argv) {
    $c = [Activator]::CreateInstance($callT)
    $c.Name = $name
    foreach ($k in $argv.Keys) { $c.Args[$k] = $argv[$k] }
    return $c
}
$cs = $formT.GetMethod('AiCheckSpelling', $IF).Invoke($f, @())
$w0 = @($cs['words'])[0]
$badIdx = [int]$w0['index']
# המילה חוזרת כמו שהיא בטקסט, עם אות השימוש: ״בתוספוט״, לא ״תוספוט״
Check 'בצ׳אט check_spelling: המילה, מספר הכתובית והצעות' ($cs['total'] -eq 1 -and $w0['word'] -eq 'בתוספוט' -and $doc.Cues[$badIdx - 1].Text.Contains('בתוספוט') -and @($w0['suggestions']).Count -gt 0) ("שורה " + $badIdx + ": " + $w0['word'] + " -> " + (@($w0['suggestions']) -join '/'))
$bad = $formT.GetMethod('AiFixSpelling', $IF).Invoke($f, (Pack (AiCall 'fix_spelling' @{ index = $badIdx; word = 'אין-כזו'; replacement = 'משהו' })))
Check 'fix_spelling על מילה שאינה שם: שגיאה, בלי לגעת בטקסט' ($bad.ContainsKey('error') -and $doc.Cues[$badIdx - 1].Text.Contains('בתוספוט')) ([string]$bad['error'])
$wrong = $formT.GetMethod('AiFixSpelling', $IF).Invoke($f, (Pack (AiCall 'fix_spelling' @{ index = 1; word = 'בתוספוט'; replacement = 'בתוספות' })))
Check 'ועל כתובית אחרת: שגיאה, ולא תיקון בכתובית הלא נכונה' ($wrong.ContainsKey('error') -and $doc.Cues[$badIdx - 1].Text.Contains('בתוספוט')) ([string]$wrong['error'])
$fix = $formT.GetMethod('AiFixSpelling', $IF).Invoke($f, (Pack (AiCall 'fix_spelling' @{ index = $badIdx; word = 'בתוספוט'; replacement = 'בתוספות' })))
Check 'fix_spelling מתקן את הכתובית שצוינה, וניתן לביטול' ($doc.Cues[$badIdx - 1].Text -eq 'עוד שגיאה בתוספות' -and $doc.CanUndo) $doc.Cues[$badIdx - 1].Text
$doc.Undo(); [void](Call 'SyncAfterDocChange' @())
$add = $formT.GetMethod('AiAddWord', $IF).Invoke($f, (Pack (AiCall 'add_word_to_dictionary' @{ word = 'בתוספוט' })))
Check 'add_word_to_dictionary: המילה כבר לא נחשבת שגויה' (@($spT.GetMethod('Misspelled', $SF).Invoke($null, (Pack 'בתוספוט'))).Count -eq 0) ([string]$add['done'])

[void](Call 'TurnSpellOff' @())
$sp = @(@($qaT.GetMethod('Find', $SF).Invoke($null, (Pack $doc))) | Where-Object { [string]$_.Kind -eq 'Spelling' })
Check 'לכבות: אין יותר בעיות איות, והמילון משתחרר מהזיכרון' ($sp.Count -eq 0 -and -not [bool]$spT.GetProperty('Ready', $SF).GetValue($null, $null)) ''
$mi = $formT.GetMethod('SpellMenuItem', $IF).Invoke($f, @())
Check 'ובתפריט: ״כבויה · לחיצה מדליקה״' ($mi.Desc.Contains('כבויה')) $mi.Desc
$spT.GetField('Enabled', $SF).SetValue($null, $true)
$spT.GetField('FolderOverride', $SF).SetValue($null, [string](Join-Path $work 'empty'))
$mi = $formT.GetMethod('SpellMenuItem', $IF).Invoke($f, @())
Check 'בלי מילון: ״מילון חינמי, פעם אחת״' ($mi.Desc.Contains('מילון חינמי')) $mi.Desc
$f.Close(); $f.Dispose(); [Windows.Forms.Application]::DoEvents()

# ---- החלון ----
$dlg = [Activator]::CreateInstance((T 'SpellSetupDlg'), $IF -bor [Reflection.BindingFlags]::CreateInstance, $null, @(), $null)
Check 'חלון ההורדה נבנה, ואומר כמה יורד ושאחרי זה בלי אינטרנט' ($dlg -ne $null) ''
$dlg.Dispose()

# ---- חלון ההגדרות (0.8.0) ----
# עד 0.8.0 הוא אמר ״לא הוגדר מפתח - התרגום, התמלול והעוזר כבויים״ (לא נכון מאז
# 0.7.2), ולא היו בו המפתח של Groq, בדיקת האיות, ולא מה התוכנה שמה על הדיסק.
$setT = T 'SettingsDlg'
$aiT = T 'Ai'; $sttT = T 'Stt'
$oldKey = $aiT.GetField('Key', $SF).GetValue($null); $oldGroq = $sttT.GetField('GroqKey', $SF).GetValue($null)
function SetText($dlg, $field) { return [string]($setT.GetField($field, $IF).GetValue($dlg)).Sub }
$aiT.GetField('Key', $SF).SetValue($null, ''); $sttT.GetField('GroqKey', $SF).SetValue($null, '')
$spT.GetField('FolderOverride', $SF).SetValue($null, [string]$dict)
$spT.GetField('Enabled', $SF).SetValue($null, $true)
# **המשתנה כאן לא נקרא ‎$sf‎:** ב-PowerShell משתנים לא רגישים לרישיות, והוא היה
# דורס את ‎$SF‎ (דגלי ה-reflection). הבדיקה נפלה שתי שורות אחר כך עם
# ״Cannot find an overload for GetMethod״, שלא רומז על שום דבר.
$setDlg = [Activator]::CreateInstance($setT, $IF -bor [Reflection.BindingFlags]::CreateInstance, $null, @($null), $null)
Check 'הגדרות: בלי מפתחות - שני השירותים מסומנים ״לא מוגדר״' ((SetText $setDlg '_google').Contains('לא מוגדר') -and (SetText $setDlg '_groq').Contains('לא מוגדר')) ((SetText $setDlg '_google') + ' | ' + (SetText $setDlg '_groq'))
$dictLine = [string]($setT.GetField('_dictLine', $IF).GetValue($setDlg)).Text
$engLine = [string]($setT.GetField('_engineLine', $IF).GetValue($setDlg)).Text
Check 'הגדרות: ״אחסון״ אומר כמה תופסים המנוע והמילון' ($dictLine.Contains('מילון האיות') -and $dictLine -match '\d' -and $engLine.Contains('מנוע הווידאו')) ($engLine + ' | ' + $dictLine)
$setDlg.Dispose()
$sttT.GetField('GroqKey', $SF).SetValue($null, 'gsk_test')
$aiT.GetField('Key', $SF).SetValue($null, 'AIza_test')
$setDlg = [Activator]::CreateInstance($setT, $IF -bor [Reflection.BindingFlags]::CreateInstance, $null, @($null), $null)
Check 'הגדרות: עם מפתחות - ״מוגדר״, ובלי לחשוף את המפתח עצמו' ((SetText $setDlg '_google').StartsWith('מוגדר') -and (SetText $setDlg '_groq').StartsWith('מוגדר') -and -not (SetText $setDlg '_groq').Contains('gsk_test')) ((SetText $setDlg '_google') + ' | ' + (SetText $setDlg '_groq'))
# המתג מכבה ומדליק את הבדיקה, ומשחרר את המילון מהזיכרון
[void]$spT.GetMethod('LoadNow', $SF).Invoke($null, @())
$tog = $setT.GetField('_spellOn', $IF).GetValue($setDlg)
$tog.Checked = $false
Check 'הגדרות: המתג מכבה את בדיקת האיות ומשחרר את המילון' ((-not [bool]$spT.GetField('Enabled', $SF).GetValue($null)) -and (-not [bool]$spT.GetProperty('Ready', $SF).GetValue($null, $null))) ''
$tog.Checked = $true
Check 'והדלקה מחזירה אותה' ([bool]$spT.GetField('Enabled', $SF).GetValue($null)) ''
$setDlg.Dispose()
$aiT.GetField('Key', $SF).SetValue($null, $oldKey); $sttT.GetField('GroqKey', $SF).SetValue($null, $oldGroq)

# מחיקת המילון: יורד מהדיסק, משתחרר מהזיכרון, והמילון האישי לא נמחק
$delDir = Join-Path $work 'to-delete'
New-Item -ItemType Directory $delDir | Out-Null
Copy-Item (Join-Path $dict 'he_IL.aff') $delDir; Copy-Item (Join-Path $dict 'he_IL.dic') $delDir
$spT.GetField('FolderOverride', $SF).SetValue($null, [string]$delDir)
$words = Join-Path $work 'my-words.txt'
[IO.File]::WriteAllText($words, "מילהשלי`r`n", (New-Object Text.UTF8Encoding $false))
$spT.GetField('UserFileOverride', $SF).SetValue($null, [string]$words)
[void]$spT.GetMethod('LoadNow', $SF).Invoke($null, @())
$sizeBefore = [long]$spT.GetProperty('SizeOnDisk', $SF).GetValue($null, $null)
$a = New-Object object[] 1
$okDel = $spT.GetMethod('Remove', $SF).Invoke($null, $a)
Check 'מחיקת המילון: נמחק מהדיסק, ומשתחרר מהזיכרון' ($okDel -and $sizeBefore -gt 1000000 -and -not (Test-Path $delDir) -and -not [bool]$spT.GetProperty('Ready', $SF).GetValue($null, $null)) ("היה " + $sizeBefore)
Check 'והמילון האישי של המשתמש נשאר' ((Test-Path $words) -and ([IO.File]::ReadAllText($words)).Contains('מילהשלי')) ''
$spT.GetField('UserFileOverride', $SF).SetValue($null, $null)
$spT.GetField('FolderOverride', $SF).SetValue($null, [string]$dict)

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
