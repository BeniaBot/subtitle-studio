# מייצר את הלוגו כ-PNG ברזולוציות גבוהות (לאתרים, פרסום, חנויות).
# אותו ציור בדיוק כמו אייקון התוכנה, רק וקטורי ומוגדל - בלי הגדלה של תמונה קטנה.
#
#   powershell -ExecutionPolicy Bypass -File build\make-logo.ps1
#
# הפלט: docs\logo\*.png
param([string]$Out = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Out -eq "") { $Out = Join-Path $root 'docs\logo' }
New-Item -ItemType Directory -Force $Out | Out-Null

Add-Type -AssemblyName System.Drawing

$Blue   = [System.Drawing.Color]::FromArgb(0x4C, 0x8D, 0xFF)
$Purple = [System.Drawing.Color]::FromArgb(0xA9, 0x7B, 0xFF)
$Ink    = [System.Drawing.Color]::FromArgb(0x14, 0x18, 0x22)

function RoundPath($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $p.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function NewCanvas($w, $h) {
    $b = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $b.SetResolution(144, 144)
    return $b
}

function Prep($g) {
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
}

# ---------- הסמל עצמו, בקואורדינטות 64x64 מוגדלות ----------
function DrawMark($g, $ox, $oy, $size) {
    $s = $size / 64.0
    $rect = New-Object System.Drawing.RectangleF ($ox + 2 * $s), ($oy + 2 * $s), (60 * $s), (60 * $s)
    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $Blue, $Purple, 45.0
    $p = RoundPath $rect.X $rect.Y $rect.Width $rect.Height (14 * $s)
    $g.FillPath($lg, $p)
    $p.Dispose(); $lg.Dispose()

    $w = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $p = RoundPath ($ox + 12 * $s) ($oy + 38 * $s) (40 * $s) (7 * $s) (3.5 * $s); $g.FillPath($w, $p); $p.Dispose()
    $p = RoundPath ($ox + 20 * $s) ($oy + 49 * $s) (24 * $s) (7 * $s) (3.5 * $s); $g.FillPath($w, $p); $p.Dispose()
    $tri = @(
        (New-Object System.Drawing.PointF ($ox + 25 * $s), ($oy + 11 * $s)),
        (New-Object System.Drawing.PointF ($ox + 43 * $s), ($oy + 22 * $s)),
        (New-Object System.Drawing.PointF ($ox + 25 * $s), ($oy + 33 * $s))
    )
    $g.FillPolygon($w, [System.Drawing.PointF[]]$tri)
    $w.Dispose()
}

# ---------- גופן: Assistant המוטמע, ואם אין - גופן המערכת ----------
$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$family = $null
$fontDir = Join-Path $root 'assets\fonts'
if (Test-Path $fontDir) {
    foreach ($f in (Get-ChildItem $fontDir -Filter *.ttf -ErrorAction SilentlyContinue)) {
        try { $fonts.AddFontFile($f.FullName) } catch { }
    }
    foreach ($fam in $fonts.Families) { if ($fam.Name -like '*Assistant*') { $family = $fam } }
}
function MakeFont($size, $style) {
    # סוגריים חובה: בלעדיהם PowerShell שולח את המחרוזת "[single]132" לבנאי
    if ($family) { return New-Object System.Drawing.Font -ArgumentList @($family, ([single]$size), $style) }
    return New-Object System.Drawing.Font -ArgumentList @('Segoe UI', ([single]$size), $style)
}

# ---------- 1. הסמל בלבד, רקע שקוף ----------
foreach ($px in @(1024, 512, 256, 128, 64)) {
    $b = NewCanvas $px $px
    $g = [System.Drawing.Graphics]::FromImage($b); Prep $g
    DrawMark $g 0 0 $px
    $g.Dispose()
    $path = Join-Path $Out ("logo-$px.png")
    $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    Write-Host ("  logo-$px.png")
}

# ---------- 2. הסמל עם השם, לרוחב ----------
function FitFont($g, $text, $maxW, $start, $style) {
    # מקטינים עד שהטקסט נכנס - עדיף גופן קטן במעט מאשר שם חתוך
    $size = $start
    while ($size -gt 12) {
        $f = MakeFont $size $style
        $w = $g.MeasureString($text, $f).Width
        if ($w -le $maxW) { return $f }
        $f.Dispose()
        $size = $size - 2
    }
    return (MakeFont 12 $style)
}

function Wordmark($file, $fg, $bg, $tag) {
    $W = 2000; $H = 560
    $mark = 380
    $pad = 90
    $b = NewCanvas $W $H
    $g = [System.Drawing.Graphics]::FromImage($b); Prep $g
    if ($null -ne $bg) { $g.Clear($bg) }

    # RTL: הסמל מימין, הטקסט משמאלו
    DrawMark $g ($W - $pad - $mark) (($H - $mark) / 2) $mark

    $textW = $W - $pad - $mark - 140 - $pad
    $title = New-Object System.Drawing.SolidBrush $fg
    $sub   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, $fg.R, $fg.G, $fg.B))
    $fT = FitFont $g 'אולפן הכתוביות' $textW 140 ([System.Drawing.FontStyle]::Bold)
    $fS = $null
    if ($tag -ne '') { $fS = FitFont $g $tag $textW 54 ([System.Drawing.FontStyle]::Regular) }

    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Far
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $sf.FormatFlags = [System.Drawing.StringFormatFlags]::DirectionRightToLeft

    $hT = $g.MeasureString('אולפן הכתוביות', $fT).Height
    $hS = 0; $gap = 0
    if ($fS) { $hS = $g.MeasureString($tag, $fS).Height; $gap = 14 }
    $top = ($H - ($hT + $gap + $hS)) / 2

    $g.DrawString('אולפן הכתוביות', $fT, $title,
        (New-Object System.Drawing.RectangleF $pad, $top, $textW, $hT), $sf)
    if ($fS) {
        $g.DrawString($tag, $fS, $sub,
            (New-Object System.Drawing.RectangleF $pad, ($top + $hT + $gap), $textW, $hS), $sf)
        $fS.Dispose()
    }

    $title.Dispose(); $sub.Dispose(); $fT.Dispose(); $g.Dispose()
    $p = Join-Path $Out $file
    $b.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    Write-Host ("  $file")
}

# בלי שורת מכירה - זו הגרסה הבטוחה לכל שימוש
Wordmark 'logo-name.png'           $Ink                            $null ''
Wordmark 'logo-name-light.png'     ([System.Drawing.Color]::White) $null ''
Wordmark 'logo-wordmark.png'       $Ink                            $null 'כתוביות בעברית, בלי כאב ראש'
Wordmark 'logo-wordmark-light.png' ([System.Drawing.Color]::White) $null 'כתוביות בעברית, בלי כאב ראש'

# ---------- 3. ריבוע חברתי עם רקע (לפרופיל / תמונת שיתוף) ----------
$b = NewCanvas 1200 1200
$g = [System.Drawing.Graphics]::FromImage($b); Prep $g
$g.Clear([System.Drawing.Color]::FromArgb(0xF7, 0xF8, 0xFA))
DrawMark $g 240 200 720
$fT = FitFont $g 'אולפן הכתוביות' 1020 100 ([System.Drawing.FontStyle]::Bold)
$br = New-Object System.Drawing.SolidBrush $Ink
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.FormatFlags = [System.Drawing.StringFormatFlags]::DirectionRightToLeft
$g.DrawString('אולפן הכתוביות', $fT, $br, (New-Object System.Drawing.RectangleF 60, 980, 1080, 140), $sf)
$fT.Dispose(); $br.Dispose(); $g.Dispose()
$b.Save((Join-Path $Out 'logo-square.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$b.Dispose()
Write-Host '  logo-square.png'

Write-Host ""
Write-Host "logos: $Out"
