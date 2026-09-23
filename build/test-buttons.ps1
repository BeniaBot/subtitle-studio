# כל כפתור בכל חלון: התוכן ממורכז, לא נחתך, ולא נוגע בקצה.
#
# **למה:** בנימין שם לב ש״יש מעט שגיאות UI ששרדו מגרסאות מוקדמות, בעיקר דברים
# לא סימטריים או לא ממורכזים בתוך הכפתור שלהם״. אף בדיקה לא ראתה את זה:
# test-layout בודקת שפקדים לא חופפים ולא גולשים, לא איפה התוכן **בתוך** פקד.
# הסקירה הראשונה מצאה 151 כפתורים, ובהם:
# - ״ביטול״ בכל חלון: 94 פיקסלים משמאל לטקסט מול 15 מימין (Btn הצמיד הכול לימין).
# - ״מהסרט״: קיבל רוחב של אייקון, שמר מקום לטקסט, והאייקון צויר חצי מחוץ לכפתור.
# - כרטיסים עם שורת הסבר: הגוש גבוה ב-10 פיקסלים.
#
# **איך:** כל כפתור מצויר לבד לתמונה. הרקע הוא הצבע הנפוץ בפנים, וכל פיקסל שרחוק
# ממנו הוא ״דיו״. משווים את השוליים משני הצדדים.
#
# **מה מיושר לימין בכוונה** ולא נבדק לאופק: כרטיס עם שורת הסבר (רשימת הכלים),
# שורת קישור (Kind=Tool), כפתור תפריט, דגימת צבע, ו-AlignRight.
#
# **סובלנות:** 8 פיקסלים ב-125% לרוחב. אייקון מצויר בתוך ריבוע עם שוליים משלו
# (חץ צר, למשל), ואות כמו ״ק״ יורדת מתחת לשורה - זה לא באג.
# צפוי: 10 בדיקות.
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class InkBox {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    // מחזיר {שמאל, ימין, עליון, תחתון} של הדיו בתוך השוליים, או null אם אין
    public static int[] Measure(Bitmap b, int m) {
        int w = b.Width, h = b.Height;
        BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int[] px = new int[w * h];
        for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), px, y * w, w);
        b.UnlockBits(d);
        var hist = new System.Collections.Generic.Dictionary<int, int>();
        for (int y = m; y < h - m; y += 2) for (int x = m; x < w - m; x += 2) { int k = px[y * w + x]; int c; hist.TryGetValue(k, out c); hist[k] = c + 1; }
        int bg = 0, best = -1;
        foreach (var kv in hist) if (kv.Value > best) { best = kv.Value; bg = kv.Key; }
        int L = w, R = -1, T = h, B = -1;
        for (int y = m; y < h - m; y++) for (int x = m; x < w - m; x++) {
            int p = px[y * w + x];
            int dd = Math.Abs(((p >> 16) & 255) - ((bg >> 16) & 255)) + Math.Abs(((p >> 8) & 255) - ((bg >> 8) & 255)) +
                     Math.Abs((p & 255) - (bg & 255)) + Math.Abs(((p >> 24) & 255) - ((bg >> 24) & 255));
            if (dd > 90) { if (x < L) L = x; if (x > R) R = x; if (y < T) T = y; if (y > B) B = y; }
        }
        return R < 0 ? null : new int[] { L, w - 1 - R, T, h - 1 - B };
    }
}
'@
[void][InkBox]::SetProcessDPIAware()
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
$IN = [Reflection.BindingFlags]'NonPublic,Public,Instance'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
function NewOf($n, $argv) { return [Activator]::CreateInstance((TY $n), $IN -bor [Reflection.BindingFlags]::CreateInstance, $null, $argv, $null) }
$pass = 0; $fail = 0
function Check($n, $ok, $d) { if ($ok) { $script:pass++; Write-Host "  ok    $n   $d" } else { $script:fail++; Write-Host "  FAIL  $n   $d" -ForegroundColor Red } }

$scale = 1.25
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]$scale)

# כל המדידות כאן הן של טקסט מצויר. בלי הגופן המוטמע הכול נמדד ב-Segoe UI,
# שרחב ממנו, והבדיקה מדווחת על כפתורים צרים שבתוכנה האמיתית תקינים. זה קרה:
# test-run.ps1 בנה עד 0.8.1 קובץ בלי הגופנים ודרס את dist.
$fontReady = (TY 'Fonts').GetProperty('Ready', $ST).GetValue($null, $null)
Check 'הגופן המוטמע נטען (אחרת כל מדידה כאן היא של גופן אחר)' $fontReady ((TY 'Fonts').GetField('FaceRegular', $ST).GetValue($null))
if (-not $fontReady) { Write-Host "`n$pass passed, $fail failed"; exit 1 }
function S([double]$v) { return [int][Math]::Round($v * $scale) }

# ---- חומרים ----
$doc = NewOf 'Doc' @()
$dcues = (TY 'Doc').GetField('Cues', $IN).GetValue($doc)
foreach ($c in @(@(1000,4000,'שלום לכולם'), @(5500,9000,'This is a test line'))) { $dcues.Add((NewOf 'Cue' @([int64]$c[0], [int64]$c[1], [string]$c[2]))) }
$style = NewOf 'SubStyle' @()
$media = Join-Path $env:TEMP 'ss-gallery\test.mp4'
if (-not (Test-Path $media)) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-testmedia.ps1') | Out-Null }
[void](TY 'Runtime').GetMethod('Prepare', $ST).Invoke($null, @())
$mi = (TY 'Ff').GetMethod('ProbeFile', $ST).Invoke($null, @([string]$media))
$main = [Activator]::CreateInstance((TY 'MainForm'))
function ToolNamed($like) {
    $all = (TY 'MediaTools').GetMethod('All', $ST).Invoke($null, @())
    foreach ($x in $all) { if ((TY 'MediaTool').GetField('Name', $IN).GetValue($x) -like $like) { return $x } }
    return $all[0]
}
$rel = [Activator]::CreateInstance((TY 'Updater+Release')); $rel.Version = '0.9.9'; $rel.Notes = 'x'
$sum = (TY 'Changelog').GetMethod('Build', $ST).Invoke($null, @([string][IO.File]::ReadAllText((Join-Path $root 'changelog.json'), [Text.Encoding]::UTF8), [string]'0.1.0', [string]'0.7.0'))

$forms = @(
    @{ n = 'ImportText';  make = { NewOf 'ImportTextDlg' @([int64]0, $true) } },
    @{ n = 'Transcribe';  make = { NewOf 'TranscribeDlg' @($mi, [int]3) } },
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
    @{ n = 'SpellSetup';  make = { NewOf 'SpellSetupDlg' @() } },
    @{ n = 'AiTranslate'; make = { NewOf 'AiTranslateDlg' @($doc) } },
    @{ n = 'Update';      make = { NewOf 'UpdateDlg' @($rel, $sum) } },
    @{ n = 'Trim';        make = { NewOf 'TrimDlg' @($null, $mi, $doc, [int64]5000, [int64]15000) } },
    @{ n = 'Extract';     make = { NewOf 'ExtractSubsDlg' @($null, $mi) } },
    @{ n = 'Tools';       make = { NewOf 'ToolsDlg' @($null, $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Export';      make = { NewOf 'ExportVideoDlg' @($null, $doc, $mi, $style, [int64]-1, [int64]-1) } },
    @{ n = 'FitSize';     make = { NewOf 'ToolRunDlg' @($null, (ToolNamed '*גודל קובץ*'), $mi, [int64]-1, [int64]-1, [int64]0) } },
    @{ n = 'Main-1500';   make = { $main.Size = New-Object Drawing.Size 1500, 950; $main } },
    @{ n = 'Main-1000';   make = { $main.Size = New-Object Drawing.Size 1000, 700; $main } }
)

$btnT = TY 'Btn'
$createCtl = [Windows.Forms.Control].GetMethod('CreateControl', $IN, $null, [Type[]]@([bool]), $null)
$getState = [Windows.Forms.Control].GetMethod('GetState', $IN, $null, [Type[]]@([int]), $null)
function Walk($c) { foreach ($k in $c.Controls) { $k; Walk $k } }
# נראות פנימית: Visible מחזיר false כל עוד החלון לא הוצג (STATE_VISIBLE = 0x2)
function Shown($c, $top) { $p = $c; while ($p -ne $null -and $p -ne $top) { if (-not $getState.Invoke($p, @([int]2))) { return $false }; $p = $p.Parent }; return $true }

$all = New-Object Collections.ArrayList
foreach ($f in $forms) {
    $form = & $f.make
    $form.StartPosition = 'Manual'; $form.Location = New-Object Drawing.Point -6000, 0
    [void]$form.Handle
    [void]$createCtl.Invoke($form, @($true))
    $form.PerformLayout()
    foreach ($c in (Walk $form)) {
        if (-not $btnT.IsInstanceOfType($c)) { continue }
        if (-not (Shown $c $form) -or $c.Width -lt 8 -or $c.Height -lt 8) { continue }
        $bmp = New-Object Drawing.Bitmap $c.Width, $c.Height
        $c.DrawToBitmap($bmp, (New-Object Drawing.Rectangle 0, 0, $c.Width, $c.Height))
        $m = S 4
        $ink = [InkBox]::Measure($bmp, $m)
        $bmp.Dispose()
        if ($ink -eq $null) { continue }
        $hasText = (-not $c.IconOnly) -and $c.Text
        $label = if ($c.Text) { $c.Text } else { '(' + $c.Icon + ')' }
        # כמה מקום יש לטקסט, באותו חשבון ש-Btn מצייר
        $avail = $c.Width - 2 * $(if ($c.PadX -ge 0) { $c.PadX } else { S 12 })
        if ($c.Icon -ne 'None') { $avail -= $c.IconSize + (S 8) }
        if ($c.Menu) { $avail -= S 16 }
        if (-not $c.Swatch.IsEmpty) { $avail -= (S 22) + (S 8) }
        $font = if ($c.Sub) { (TY 'Theme').GetProperty('UiBold', $ST).GetValue($null, $null) } else { $c.Font }
        $tw = if ($hasText) { [Windows.Forms.TextRenderer]::MeasureText($c.Text, $font, (New-Object Drawing.Size 100000, 1000), [Windows.Forms.TextFormatFlags]'NoPadding,NoPrefix').Width } else { 0 }
        [void]$all.Add([pscustomobject]@{
            Where = $f.n; Label = $label; W = $c.Width; H = $c.Height
            Centered = $hasText -and -not $c.Sub -and -not $c.Menu -and [string]$c.Kind -ne 'Tool' -and -not $c.AlignRight -and $c.Swatch.IsEmpty
            IconOnly = -not $hasText; Card = [bool]$c.Sub
            L = $ink[0]; R = $ink[1]; T = $ink[2]; B = $ink[3]; M = $m
            TextW = $tw; Avail = $avail; HasText = [bool]$hasText
            # כפתור מלא בצורת עיגול או גלולה (הניגון): הרקע עצמו מגיע עד הקצה
            Round = ($c.Radius * 2 -ge $c.Height - 2) -and ([string]$c.Kind -eq 'Primary')
        })
    }
    if ($f.n -notlike 'Main*') { $form.Dispose() }
}

function Report($list, $fmt) { if ($list.Count -eq 0) { return '' }; return "`n     " + (($list | Select-Object -First 12 | ForEach-Object { & $fmt $_ }) -join "`n     ") }

Check 'נמצאו כפתורים בכל החלונות' ($all.Count -ge 140 -and @($all | Select-Object -ExpandProperty Where -Unique).Count -eq $forms.Count) ("כפתורים: " + $all.Count)

$bad = @($all | Where-Object { $_.Centered -and [Math]::Abs($_.L - $_.R) -gt (S 6) })
Check 'כפתור עם טקסט: התוכן ממורכז לרוחב' ($bad.Count -eq 0) (Report $bad { param($x) "{0}: «{1}» שמאל {2} ימין {3}" -f $x.Where, $x.Label, $x.L, $x.R })

$bad = @($all | Where-Object { $_.IconOnly -and [Math]::Abs($_.L - $_.R) -gt (S 3) })
Check 'כפתור אייקון: האייקון ממורכז' ($bad.Count -eq 0) (Report $bad { param($x) "{0}: {1} שמאל {2} ימין {3}" -f $x.Where, $x.Label, $x.L, $x.R })

$bad = @($all | Where-Object { ($_.IconOnly -or $_.Centered) -and [Math]::Abs($_.T - $_.B) -gt (S 5) })
Check 'כפתור: התוכן ממורכז לגובה' ($bad.Count -eq 0) (Report $bad { param($x) "{0}: «{1}» מעל {2} מתחת {3}" -f $x.Where, $x.Label, $x.T, $x.B })

$bad = @($all | Where-Object { $_.Card -and [Math]::Abs($_.T - $_.B) -gt (S 4) })
Check 'כרטיס עם שורת הסבר: שתי השורות ממורכזות לגובה' ($bad.Count -eq 0) (Report $bad { param($x) "{0}: «{1}» מעל {2} מתחת {3}" -f $x.Where, $x.Label, $x.T, $x.B })

# ״מהסרט״: תוכן שנוגע בשוליים הפנימיים = נחתך או יוצא מהכפתור
$bad = @($all | Where-Object { -not $_.Round -and ($_.L -le $_.M -or $_.R -le $_.M) })
Check 'שום תוכן לא נוגע בקצה הכפתור' ($bad.Count -eq 0) (Report $bad { param($x) "{0}: «{1}» {2}x{3} שמאל {4} ימין {5}" -f $x.Where, $x.Label, $x.W, $x.H, $x.L, $x.R })

$bad = @($all | Where-Object { $_.HasText -and $_.TextW -gt $_.Avail + 1 })
Check 'טקסט נכנס לכפתור שלו (בלי ״...״)' ($bad.Count -eq 0) (Report $bad { param($x) "{0}: «{1}» צריך {2} ויש {3}" -f $x.Where, $x.Label, $x.TextW, $x.Avail })

# כפתור המהירות, בכל מהירות - לא רק ״1×״ שבמקרה נכנס. עד 0.7.2 ״1.25×״ הוצג ״1…״.
$mfT = TY 'MainForm'
$sp = $mfT.GetField('_speedBtn', $IN).GetValue($main)
$speeds = $mfT.GetField('Speeds', $ST).GetValue($null)
$spText = $mfT.GetMethod('SpeedText', $ST)
$cut = @()
foreach ($size in @(@(1500, 950), @(1000, 700))) {
    $main.Size = New-Object Drawing.Size $size[0], $size[1]; $main.PerformLayout()
    foreach ($v in $speeds) {
        $t = [string]$spText.Invoke($null, @([double]$v))
        if ($sp.IconOnly) { continue }
        $need = [Windows.Forms.TextRenderer]::MeasureText($t, $sp.Font, (New-Object Drawing.Size 100000, 1000), [Windows.Forms.TextFormatFlags]'NoPadding,NoPrefix').Width
        $have = $sp.Width - 2 * (S 12) - $sp.IconSize - (S 8) - (S 16)
        if ($need -gt $have + 1) { $cut += ("{0}x{1}: {2} צריך {3} ויש {4}" -f $size[0], $size[1], $t, $need, $have) }
    }
}
Check 'כפתור המהירות: כל מהירות נכנסת' ($cut.Count -eq 0) ("`n     " + ($cut -join "`n     "))

$n = @($all | Where-Object Centered).Count
Check 'רוב הכפתורים ממורכזים, והמיושרים לימין הם רשימות' ($n -ge 60) ("ממורכזים: $n, כרטיסים: " + @($all | Where-Object Card).Count + ", אייקון: " + @($all | Where-Object IconOnly).Count)

$main.Dispose()
Write-Host ''
Write-Host ('{0} passed, {1} failed' -f $pass, $fail)
if ($fail -gt 0) { exit 1 }
