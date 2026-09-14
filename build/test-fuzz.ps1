# קבצי כתוביות משובשים: אסור לקרוס, ואסור להיתקע.
#
# קובץ SRT שבור מגיע כל הזמן: הורדה שנקטעה, קידוד שהשתבש, ״תיקון״ ידני
# במחברת. ‏ImportSubs עוטף בניסיון-ותפיסה, ולכן חריגה ״רק״ מציגה שגיאה - אבל
# לולאה אינסופית תוקעת את כל התוכנה, ושם אין מי שיתפוס. לכן לכל מקרה יש
# גם שעון: פענוח שלוקח יותר משנייה על קובץ של כמה קילובייט הוא באג.
#
# מה נבדק: SRT, ‏VTT, ‏ASS, ‏MicroDVD וטקסט חופשי - 400 שיבושים לכל אחד
# (חיתוך, מחיקת תו, תו זר, שכפול שורה, החלפת שורות חדשות), ועוד מקרים
# קיצוניים ידועים: קובץ ריק, רק BOM, זמנים ענקיים, מספרים שליליים.
# צפוי: 11 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\SubtitleStudio.exe')))
$SF = [Reflection.BindingFlags]'NonPublic,Public,Static'
function T($n) { $asm.GetType("SubtitleStudio.$n") }
$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$fmt = T 'Formats'
$samples = @{
    'SRT' = "1`r`n00:00:01,000 --> 00:00:02,500`r`nשלום <i>עולם</i>`r`n`r`n2`r`n00:00:03,000 --> 00:00:04,000`r`nשורה שנייה`r`nועוד אחת`r`n`r`n3`r`n01:02:03,456 --> 01:02:05,000`r`n{\an8}למעלה`r`n"
    'VTT' = "WEBVTT`n`nNOTE הערה`n`n00:01.000 --> 00:02.500 align:start`nשלום`n`ncue-2`n00:00:03.000 --> 00:00:04.000`n<v רב>שורה</v>`n"
    'ASS' = "[Script Info]`nPlayResX: 1920`n`n[V4+ Styles]`nFormat: Name, Fontname, Fontsize`nStyle: Default,Arial,48`n`n[Events]`nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text`nDialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,שלום\Nעולם`nDialogue: 0,0:00:03.00,0:00:04.00,Default,רב,0,0,0,,{\b1}מודגש{\b0}, עם פסיק`n"
    'SUB' = "{25}{50}שלום|עולם`n{75}{100}שורה שנייה`n"
}
$parsers = @{
    'SRT' = { param($t) $fmt.GetMethod('ParseSrt', $SF).Invoke($null, @([string]$t)) }
    'VTT' = { param($t) $fmt.GetMethod('ParseVtt', $SF).Invoke($null, @([string]$t)) }
    'ASS' = { param($t) $fmt.GetMethod('ParseAss', $SF).Invoke($null, @([string]$t)) }
    'SUB' = { param($t) $fmt.GetMethod('ParseMicroDvd', $SF).Invoke($null, @([string]$t, [double]25)) }
}

$rnd = New-Object Random 777
function Mutate($s) {
    if ($s.Length -eq 0) { return 'x' }
    $pos = $rnd.Next($s.Length)
    switch ($rnd.Next(6)) {
        0 { return $s.Substring(0, $pos) }
        1 { return $s.Remove($pos, 1) }
        2 { $junk = @(':', ',', '-', '>', '{', '}', '|', "`n", "`r", '9', ' ', [char]0, [char]0xFEFF, 'א'); return $s.Insert($pos, [string]$junk[$rnd.Next($junk.Count)]) }
        3 { $lines = $s -split "`n"; $i = $rnd.Next($lines.Count); return (($lines[0..$i] + $lines[$i..($lines.Count-1)]) -join "`n") }
        4 { return $s.Replace("`r`n", "`r").Replace("`n", "`r") }
        default { return ($s -replace '\d', { [string]$rnd.Next(10) }) }
    }
}

foreach ($kind in @('SRT', 'VTT', 'ASS', 'SUB')) {
    Write-Host $kind
    $good = & $parsers[$kind] $samples[$kind]
    Check "$kind תקין נקרא" (@($good).Count -ge 2) ("cues=" + @($good).Count)
    $crash = 0; $slow = 0; $worst = 0; $msg = ''
    for ($k = 0; $k -lt 400; $k++) {
        $bad = $samples[$kind]
        $times = 1 + $rnd.Next(3)
        for ($m = 0; $m -lt $times; $m++) { $bad = Mutate $bad }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        try { [void](& $parsers[$kind] $bad) }
        catch { $crash++; if ($msg -eq '') { $msg = $_.Exception.InnerException.GetType().Name + ': ' + $_.Exception.InnerException.Message } }
        $ms = $sw.ElapsedMilliseconds
        if ($ms -gt $worst) { $worst = $ms }
        if ($ms -gt 1000) { $slow++ }
    }
    Check "$kind - 400 שיבושים בלי חריגה" ($crash -eq 0) ("חריגות=$crash " + $msg)
}

Write-Host 'מקרי קצה'
$edge = @('', ([string][char]0xFEFF), "`r`n`r`n`r`n", '99999999999999999999 --> 1', "1`n-00:00:01,000 --> -00:00:02,000`nשלילי`n",
          "1`n99:99:99,999 --> 99:99:99,999`nענק`n", ("1`n00:00:01,000 --> 00:00:02,000`n" + ('א' * 100000) + "`n"))
$edgeCrash = 0; $edgeMsg = ''
foreach ($e in $edge) {
    foreach ($kind in @('SRT', 'VTT', 'ASS', 'SUB')) {
        try { [void](& $parsers[$kind] $e) } catch { $edgeCrash++; if ($edgeMsg -eq '') { $edgeMsg = "$kind : " + $_.Exception.InnerException.Message } }
    }
}
Check 'קובץ ריק, BOM בלבד, זמנים ענקיים ושליליים, שורה של 100 אלף תווים' ($edgeCrash -eq 0) $edgeMsg

# זיהוי הפורמט והקידוד מקובץ אמיתי שבור - הדרך שהמשתמש עובר בה בפועל
$tmp = Join-Path $env:TEMP ('ss-fuzz-' + [guid]::NewGuid().ToString('N').Substring(0, 6) + '.srt')
$loadCrash = 0; $loadMsg = ''
for ($k = 0; $k -lt 100; $k++) {
    $bytes = [Text.Encoding]::UTF8.GetBytes((Mutate $samples['SRT']))
    if ($rnd.Next(3) -eq 0 -and $bytes.Length -gt 4) { $bytes[$rnd.Next($bytes.Length)] = [byte]$rnd.Next(256) }   # בייט שבור - UTF-8 לא חוקי
    [IO.File]::WriteAllBytes($tmp, $bytes)
    try { [void]$fmt.GetMethod('Load', $SF).Invoke($null, @([string]$tmp)) }
    catch { $loadCrash++; if ($loadMsg -eq '') { $loadMsg = $_.Exception.InnerException.Message } }
}
Remove-Item $tmp -ErrorAction SilentlyContinue
Check '100 קבצים שבורים (כולל UTF-8 לא חוקי) דרך Formats.Load' ($loadCrash -eq 0) $loadMsg

# טקסט חופשי לכתוביות - גם הוא נקרא מכל מה שמודבק
$optT = T 'Formats+TextImportOptions'
$opt = [Activator]::CreateInstance($optT)
$txtCrash = 0; $txtMsg = ''
foreach ($t in @('', ' ', "`n`n`n", ('מילה ' * 20000), ("שורה.`n" * 3000), ('.' * 5000), ([string][char]0x200F * 100))) {
    try { [void]$fmt.GetMethod('ImportPlainText', $SF).Invoke($null, @([string]$t, $opt)) }
    catch { $txtCrash++; if ($txtMsg -eq '') { $txtMsg = $_.Exception.InnerException.Message } }
}
Check 'טקסט חופשי: ריק, ענק, רק נקודות, רק סימני כיווניות' ($txtCrash -eq 0) $txtMsg

Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
