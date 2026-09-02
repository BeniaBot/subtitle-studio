# מצייר את כל האייקונים של התוכנה לגיליון אחד לבדיקה חזותית
Add-Type -AssemblyName System.Drawing
$dest = "$env:TEMP\ss-gallery"
$asm = [Reflection.Assembly]::LoadFrom("D:\Claude\subtitle-studio\dist\SubtitleStudio.exe")
$NP = [Reflection.BindingFlags]::NonPublic
$PB = [Reflection.BindingFlags]::Public
$ST = [Reflection.BindingFlags]::Static
$icons = $asm.GetType("SubtitleStudio.Ico")
$draw = $asm.GetType("SubtitleStudio.Icons").GetMethod("Draw", ($NP -bor $PB -bor $ST), $null,
    @([System.Drawing.Graphics], $icons, [System.Drawing.RectangleF], [System.Drawing.Color], [float]), $null)

$names = [Enum]::GetNames($icons) | Where-Object { $_ -ne "None" }
$cols = 8
$cell = 92
$rows = [math]::Ceiling($names.Count / $cols)
$bmp = New-Object System.Drawing.Bitmap(($cols * $cell), ($rows * $cell))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(20, 22, 27))
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'ClearTypeGridFit'
$font = New-Object System.Drawing.Font("Segoe UI", 8)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 160, 175))
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = 'Center'

for ($i = 0; $i -lt $names.Count; $i++) {
    $c = $i % $cols; $r = [math]::Floor($i / $cols)
    $x = $c * $cell; $y = $r * $cell
    $val = [Enum]::Parse($icons, $names[$i])
    $box = New-Object System.Drawing.RectangleF(($x + 30), ($y + 16), 32, 32)
    $col = [System.Drawing.Color]::FromArgb(232, 235, 242)
    [void]$draw.Invoke($null, @($g, $val, $box, $col, [float]2))
    $g.DrawString($names[$i], $font, $brush, (New-Object System.Drawing.RectangleF($x, ($y + 56), $cell, 20)), $fmt)
}
$g.Dispose()
$bmp.Save("$dest\icons.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "icons: $dest\icons.png  ($($names.Count) icons)"
