# כל חלון וכל תפריט מול מסכים אמיתיים - לא מול המסך של המחשב הזה.
#
# **למה:** תפריט הכתוביות היה גבוה מהמסך מאז 0.6.4 ואף בדיקה לא ראתה,
# כי כולן מדדו מול מסך 1920x1200 של מחשב הפיתוח. והקוד שאמור להגן -
# ‏Dlg.Buttons מקצר חלון לגובה המסך - **לא הזיז את הכפתורים**: הם נשארו
# מתחת לקצה, ואי אפשר היה ללחוץ ״אישור״.
#
# **איך:** המחשב הזה ב-125%. בונים כל חלון בקנה המידה האמיתי, מודדים את
# הגובה **הטבעי** שלו (לפני הקיצוץ), וממירים ליחידות לוגיות. אז משווים
# לתקציב של כל מסך נפוץ:
#   1920x1080 ב-150%  -> 1040/1.5 = 693 לוגי   (הצפוף מכולם)
#   1366x768  ב-100%  -> 728 לוגי
#   1920x1080 ב-125%  -> 1040/1.25 = 832 לוגי
# ובסוף בודקים שחלון שכן חורג - הכפתורים שלו נשארים גלויים.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class DpiS { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
[void][DpiS]::SetProcessDPIAware()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\SubtitleStudio.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }

$pass=0; $fail=0
function Check($n,$ok,$d){ if($ok){$script:pass++;Write-Host "  ok    $n   $d"}else{$script:fail++;Write-Host "  FAIL  $n   $d" -ForegroundColor Red} }

$scale = 1.25
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]$scale)
$budgets = @(@{ n = '1080p ב-150%'; h = 693; w = 1280 }, @{ n = '1366x768'; h = 728; w = 1366 }, @{ n = '1080p ב-125%'; h = 832; w = 1536 })

# ---- חומרים לחלונות ----
$doc = NewOf 'Doc' @()
$dcues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
foreach ($c in @(@(1000,4000,'שלום לכולם'), @(5500,9000,'This is a test line'), @(11000,15000,'הכתובית השלישית'))) {
    $dcues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2])))
}
$style = NewOf 'SubStyle' @()
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $media)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }
(TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$mi = (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$media))
Check 'סרט הבדיקה נקרא' ($mi -ne $null -and $mi.DurationSec -gt 0) ''
$main = [Activator]::CreateInstance((TY 'MainForm'))
function ToolNamed($like) {
    $all = (TY 'MediaTools').GetMethod('All', $ST).Invoke($null, @())
    foreach ($x in $all) { if ((TY 'MediaTool').GetField('Name', $IN).GetValue($x) -like $like) { return $x } }
    return $all[0]
}
$rel = [Activator]::CreateInstance((TY 'Updater+Release'))
$rel.Version = '0.9.9'; $rel.Notes = "**שורה**`n* אחת`n* שתיים"
$json = [IO.File]::ReadAllText((Join-Path $root 'changelog.json'), [Text.Encoding]::UTF8)
$sum = (TY 'Changelog').GetMethod('Build', $ST).Invoke($null, @([string]$json, [string]'0.1.0', [string]'0.7.0'))

$dialogs = @(
    @{ n = 'ImportText';  make = { NewOf 'ImportTextDlg' @([int64]0, $true) } },
    @{ n = 'Transcribe';  make = { NewOf 'TranscribeDlg' @($mi, [int]3) } },
    @{ n = 'Fix';         make = { NewOf 'FixDlg' @($doc) } },
    @{ n = 'Sync';        make = { NewOf 'SyncDlg' @($doc, [int64]7900, (NewOf 'SyncState' @())) } },
    @{ n = 'Fps';         make = { NewOf 'FpsDlg' @($doc) } },
    @{ n = 'Shift';       make = { NewOf 'ShiftDlg' @($doc, [int64]5000) } },
    @{ n = 'Replace';     make = { NewOf 'ReplaceDlg' @($doc) } },
    @{ n = 'Find';        make = { NewOf 'FindDlg' @([string]'שלום', [int]3) } },
    @{ n = 'Style';       make = { NewOf 'StyleDlg' @($style, $null) } },
    @{ n = 'Help';        make = { NewOf 'HelpDlg' @() } },
    @{ n = 'About';       make = { NewOf 'AboutDlg' @() } },
    @{ n = 'Settings';    make = { NewOf 'SettingsDlg' @($main) } },
    @{ n = 'AiSetup';     make = { NewOf 'AiSetupDlg' @() } },
    @{ n = 'GroqSetup';   make = { NewOf 'GroqSetupDlg' @() } },
    @{ n = 'AiTranslate'; make = { NewOf 'AiTranslateDlg' @($doc) } },
    @{ n = 'Update';      make = { NewOf 'UpdateDlg' @($rel, $sum) } },
    @{ n = 'Trim';        make = { NewOf 'TrimDlg' @($null, $mi, $doc, [int64]5000, [int64]15000) } },
    @{ n = 'Extract';     make = { NewOf 'ExtractSubsDlg' @($null, $mi) } },
    @{ n = 'Tools';       make = { NewOf 'ToolsDlg' @($null, $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Export';      make = { NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [int64]-1, [int64]-1) } },
    @{ n = 'FitSize';     make = { NewOf 'ToolRunDlg' @($null, (ToolNamed '*גודל קובץ*'), $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Reverse';     make = { NewOf 'ToolRunDlg' @($null, (ToolNamed '*ריוורס*'), $mi, [int64]-1, [int64]-1, [int64]0) } }
)

Write-Host 'חלונות - גובה טבעי מול מסכים נפוצים'
$over = @()
foreach ($d in $dialogs) {
    $dlg = $null
    try { $dlg = & $d.make } catch { Check ("נבנה: " + $d.n) $false $_.Exception.InnerException.Message; continue }
    $dlg.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $dlg.Location = New-Object Drawing.Point -4000, -4000
    $dlg.Show(); [Windows.Forms.Application]::DoEvents()
    # הגובה הטבעי: הפקד הנמוך ביותר + ריפוד, לא ClientSize שכבר קוצץ
    $bottom = 0
    foreach ($c in $dlg.Controls) { if ($c.Visible -and $c.Bottom -gt $bottom) { $bottom = $c.Bottom } }
    $natural = [Math]::Max($dlg.ClientSize.Height, $bottom + [int][Math]::Round(22 * $scale))
    $logical = [int][Math]::Round($natural / $scale)
    $bad = @(); foreach ($b in $budgets) { if ($logical -gt $b.h) { $bad += $b.n } }
    Check ($d.n + ": " + $logical + " לוגי") ($bad.Count -eq 0) ("חורג ב: " + ($bad -join ', '))
    if ($bad.Count -gt 0) { $over += $d.n }
    $dlg.Close(); $dlg.Dispose(); [Windows.Forms.Application]::DoEvents()
}

# ---- חלון שחורג: הכפתורים חייבים להישאר גלויים ----
# מדמים מסך נמוך: מקצצים את החלון כמו ש-Buttons עושה, ובודקים שאין פקד לחיץ מחוץ לו
Write-Host 'חלון שקוצץ למסך'
$dlg = NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [int64]-1, [int64]-1)
$dlg.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$dlg.Location = New-Object Drawing.Point -4000, -4000
$dlg.Show(); [Windows.Forms.Application]::DoEvents()
$fit = $dlg.GetType().GetMethod('FitToHeight', $IN)
Check 'יש מנגנון התאמה לגובה (Dlg.FitToHeight)' ($fit -ne $null) ''
if ($fit) {
    $natural = $dlg.ClientSize.Height
    $target = [int](300 * $scale)
    [void]$fit.Invoke($dlg, @([int]$target))
    [Windows.Forms.Application]::DoEvents()
    Check 'אחרי התאמה: החלון בגובה המבוקש' ($dlg.ClientSize.Height -le $target) ("h=" + $dlg.ClientSize.Height + " target=$target natural=$natural")
    Check 'ונגלל' ($dlg.AutoScroll) ''
    $ok = ($dlg.GetType().BaseType.GetField('_bottom', $IN).GetValue($dlg))[0]
    $dlg.ScrollControlIntoView($ok)
    [Windows.Forms.Application]::DoEvents()
    Check 'אחרי גלילה: כפתור האישור בתוך החלון' ($ok.Top -ge 0 -and $ok.Bottom -le $dlg.ClientSize.Height) ("top=" + $ok.Top + " bottom=" + $ok.Bottom + " h=" + $dlg.ClientSize.Height)
    Check 'בלי פס גלילה אופקי' (-not $dlg.HorizontalScroll.Visible) ''
    [void]$fit.Invoke($dlg, @([int]100000))
    Check 'במסך גדול: חוזר לגובה הטבעי, בלי גלילה' ((-not $dlg.AutoScroll) -and $dlg.ClientSize.Height -eq $natural) ("h=" + $dlg.ClientSize.Height)
}
$dlg.Close(); $dlg.Dispose()

# ---- תפריטים ----
Write-Host 'תפריטים'
$main.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$main.Location = New-Object Drawing.Point -4000, -4000
$main.ClientSize = New-Object Drawing.Size ([int](1280 * $scale)), ([int](690 * $scale))
$main.Show(); [Windows.Forms.Application]::DoEvents()
(TY 'MainForm').GetField('_mi', $IN).SetValue($main, $mi)
$mdoc = (TY 'MainForm').GetField('_doc', $IN).GetValue($main)
$mdoc.Cues.Add((NewOf 'Cue' @([int64]1000, [int64]2000, [string]'x')))
$items = (TY 'MainForm').GetMethod('SubtitleMenuItems', $IN).Invoke($main, @())
$pmT = TY 'PopupMenu'
foreach ($b in $budgets) {
    # מתחת לסרגל: התפריט נפתח מתחת לכפתור, בערך 100 לוגי מראש המסך
    $room = [int](($b.h - 100) * $scale)
    $pm = $pmT.GetConstructors()[0].Invoke(@($items, [int]350))
    [void]$pmT.GetMethod('Arrange', $IN).Invoke($pm, @([int]$room))
    $cols = $pmT.GetProperty('Columns', $IN).GetValue($pm, $null)
    $lw = [int][Math]::Round($pm.ClientSize.Width / $scale); $lh = [int][Math]::Round($pm.ClientSize.Height / $scale)
    Check ("תפריט הכתוביות ב-" + $b.n + ": " + $cols + " עמודות, " + $lw + "x" + $lh) (($pm.ClientSize.Height -le $room) -and ($lw -le $b.w)) ''
    $pm.Dispose()
}
$main.Close(); $main.Dispose()

Write-Host ""
if ($over.Count -gt 0) { Write-Host ("חורגים: " + ($over -join ', ')) -ForegroundColor Yellow }
Write-Host ("{0} passed, {1} failed" -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
