# יוצר את app.ico של התוכנה (מצויר בקוד, בלי קבצים חיצוניים)
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'app.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $k = $s / 64.0

    $rect = New-Object System.Drawing.RectangleF(($k * 2), ($k * 2), ($k * 60), ($k * 60))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = $k * 14
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(76, 141, 255)
    $c2 = [System.Drawing.Color]::FromArgb(169, 123, 255)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $g.FillPath($brush, $path)

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    # משולש ניגון
    $pts = @(
        (New-Object System.Drawing.PointF(($k * 25), ($k * 11))),
        (New-Object System.Drawing.PointF(($k * 44), ($k * 22))),
        (New-Object System.Drawing.PointF(($k * 25), ($k * 33)))
    )
    $g.FillPolygon($white, [System.Drawing.PointF[]]$pts)

    # שתי שורות כתובית
    function Add-Bar($x, $y, $w, $h, $rad) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $dd = $rad * 2
        $p.AddArc($x, $y, $dd, $dd, 180, 90)
        $p.AddArc($x + $w - $dd, $y, $dd, $dd, 270, 90)
        $p.AddArc($x + $w - $dd, $y + $h - $dd, $dd, $dd, 0, 90)
        $p.AddArc($x, $y + $h - $dd, $dd, $dd, 90, 90)
        $p.CloseFigure()
        $g.FillPath($white, $p)
        $p.Dispose()
    }
    Add-Bar ($k * 12) ($k * 40) ($k * 40) ($k * 7) ($k * 3.5)
    Add-Bar ($k * 22) ($k * 51) ($k * 20) ($k * 7) ($k * 3.5)

    $g.Dispose()
    $brush.Dispose()
    $white.Dispose()
    $path.Dispose()
    return $bmp
}

foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
$bw.Close()
$fs.Close()
Write-Host "app.ico created:" $out
