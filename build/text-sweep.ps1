# text-sweep.ps1 - יבוא טקסט לכתוביות על אוסף קבצים: מה יוצא, ואיפה זה לא קריא (0.8.9).
#
# כמו real-sweep, אבל לטקסט: כל קובץ עובר את אותו מסלול כמו בחלון ״טקסט לכתוביות״ (קריאה עם זיהוי קידוד,
# ״אוטומטי״ לבחירה בין שורות לפסקאות, חותמות זמן, שבירת שורות) - ונמדד מה יצא:
#   כמה כתוביות, כמה מהן ארוכות מדי (יותר משתי שורות או שורה מעל 42 תווים), קצב הקריאה (תווים לשנייה;
#   מעל 20 - קשה לקרוא), ״זבל״ שנכנס ככתובית (מספור, חצים של SRT, סימני Markdown), ושורות שזמן בתחילתן.
#
#   text-sweep.ps1 [-Dir D:\Claude\_ss-text] [-Detail]
# הקבצים: D:\Claude\_ss-text (מקרי קצה) ו-real\ (טקסטים אמיתיים מהמחשב). ‏-Detail מדפיס את הכתוביות הגרועות.
param([string]$Dir = 'D:\Claude\_ss-text', [switch]$Detail)
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
$fmt = TY 'Formats'
$optT = $asm.GetType('SubtitleStudio.Formats+TextImportOptions')
$imp = ($fmt.GetMethods([Reflection.BindingFlags]'NonPublic,Public,Static') | Where-Object { $_.Name -eq 'ImportPlainText' -and $_.GetParameters().Count -eq 2 } | Select-Object -First 1)
$read = $fmt.GetMethod('ReadTextSmart', $ST)
$load = $fmt.GetMethod('Load', $ST)
$dlgT = TY 'ImportTextDlg'
$looks = $dlgT.GetMethod('LooksLikeParagraphs', $ST)
# ‏0.8.9: ההחלטה ״שורות או פסקאות״ ופירוק יחידה ארוכה עברו ל-Formats; אם יש - משתמשים בהם, כמו החלון
$auto = $fmt.GetMethod('AutoOptions', $ST)

$rxStamp = '^\s*[\[\(]?\s*\d{1,2}:\d{1,2}(:\d{1,2})?([.,]\d{1,3})?\s*[\]\)]?'
$rxJunk = '^\s*\d+\s*$|-->|^\s*#|\*\*|^\s*\d+[.)]\s'

$files = @(Get-ChildItem $Dir -File -Recurse | Where-Object { $_.Extension -match '^\.(txt|md|text)$' } | Sort-Object FullName)
$rows = @()
foreach ($f in $files) {
    $a = New-Object object[] 2; $a[0] = $f.FullName
    $text = [string]$read.Invoke($null, $a); $enc = [string]$a[1]
    $asSubs = 0
    try { $pr = $load.Invoke($null, @([string]$f.FullName)); $asSubs = $pr.Cues.Count } catch { }
    if ($optT.GetField('Auto')) {
        # ‏0.8.9: ״אוטומטי״ = כל קטע לפי הצורה שלו (Formats.BlockShape), כמו בחלון
        $o = [Activator]::CreateInstance($optT)
        $o.Auto = $true; $o.UseTimestamps = $true; $o.AutoWrap = $true; $o.Cps = 15; $o.MinDur = 1400; $o.Gap = 80; $o.StartAt = 0
    } else {
        $o = [Activator]::CreateInstance($optT)
        $o.SplitByBlankLine = [bool]$looks.Invoke($null, @([string]$text))
        $o.UseTimestamps = $true; $o.AutoWrap = $true; $o.Cps = 15; $o.MinDur = 1400; $o.Gap = 80; $o.StartAt = 0
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $cues = @($imp.Invoke($null, @([string]$text, $o)))
    $ms = $sw.ElapsedMilliseconds
    $n = $cues.Count
    $tooLong = 0; $maxLine = 0; $maxLines = 0; $fast = 0; $maxCps = 0; $junk = 0; $worst = $null; $worstScore = -1
    foreach ($c in $cues) {
        $t = [string]$c.Text
        $ls = $t -split "`n"
        if ($ls.Count -gt $maxLines) { $maxLines = $ls.Count }
        $ll = 0; foreach ($l in $ls) { if ($l.Length -gt $ll) { $ll = $l.Length } }
        if ($ll -gt $maxLine) { $maxLine = $ll }
        if ($ls.Count -gt 2 -or $ll -gt 42) { $tooLong++ }
        $chars = ($t -replace '\s', '').Length
        $sec = [Math]::Max(0.001, ($c.End - $c.Start) / 1000.0)
        $cps = $chars / $sec
        if ($cps -gt $maxCps) { $maxCps = $cps }
        if ($cps -gt 20) { $fast++ }
        if ($t -match $rxJunk) { $junk++ }
        $score = $chars + 10 * [Math]::Max(0, $ls.Count - 2)
        if ($score -gt $worstScore) { $worstScore = $score; $worst = $t }
    }
    $stampLines = @(($text -split "`n") | Where-Object { $_ -match $rxStamp }).Count
    $mode = if ($optT.GetField('Auto') -and $o.Auto) { 'אוטומטי' } elseif ($o.SplitByBlankLine) { 'פסקאות' } else { 'שורות' }
    $name = $f.FullName.Substring($Dir.Length).TrimStart('\')
    $rows += [pscustomobject]@{ קובץ = $name; קידוד = ($enc -replace ' \(.*', ''); מצב = $mode; כתוביות = $n; ארוכות = $tooLong; שורות_מרבי = $maxLines; תווים_בשורה = $maxLine; קצב_מרבי = [int]$maxCps; מהירות_מדי = $fast; זבל = $junk; זמן_בשורה = $stampLines; כקובץ_כתוביות = $asSubs; אלפיות = $ms }
    if ($Detail -and $worst) { Write-Host ("  [" + $name + "] הגרועה: " + ($worst -replace "`n", ' / ')) }
}
$rows | Format-Table -AutoSize | Out-String -Width 260 | Write-Host
$bad = @($rows | Where-Object { $_.ארוכות -gt 0 -or $_.מהירות_מדי -gt 0 -or $_.זבל -gt 0 })
Write-Host ("סיכום: " + $rows.Count + " קבצים, " + (($rows | Measure-Object כתוביות -Sum).Sum) + " כתוביות; ארוכות מדי: " + (($rows | Measure-Object ארוכות -Sum).Sum) + ", מהירות מדי: " + (($rows | Measure-Object מהירות_מדי -Sum).Sum) + ", זבל: " + (($rows | Measure-Object זבל -Sum).Sum) + "; קבצים עם בעיה: " + $bad.Count)
