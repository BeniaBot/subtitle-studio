# icon-sheet.ps1 - every Ico at the sizes the app really uses, both themes, one PNG.
# ASCII only. Output: %TEMP%\ss-shots\icons-<tag>.png
param([string]$Tag = 'now', [double]$Scale = 1.25)
$ErrorActionPreference = 'Stop'
$env:SUBSTUDIO_TEST = '1'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'dist\Subtext.exe')))
$ST = [Reflection.BindingFlags]'NonPublic,Public,Static'
function TY($n) { return $asm.GetType("SubtitleStudio.$n") }
(TY 'Theme').GetField('Scale', $ST).SetValue($null, [float]$Scale)

$icoT  = TY 'Ico'
$draw  = (TY 'Icons').GetMethod('Draw', $ST, $null, [Type[]]@([Drawing.Graphics], $icoT, [Drawing.RectangleF], [Drawing.Color], [float]), $null)
$smooth = (TY 'Theme').GetMethod('Smooth', $ST)
$names = @([Enum]::GetNames($icoT) | Where-Object { $_ -ne 'None' })
# logical sizes used across the app: tool 15, button 18, hero chip 19, big 22
$sizes = @(15, 18, 19, 22)
$cell = [int][Math]::Round(40 * $Scale)
$cols = 12
$rows = [Math]::Ceiling($names.Count / $cols)
$blockW = $cols * $cell
$blockH = $rows * $cell * $sizes.Count
$bmp = New-Object Drawing.Bitmap ($blockW * 2 + 20), $blockH
$g = [Drawing.Graphics]::FromImage($bmp)
$themes = @(@{ dark = $true; x = 0 }, @{ dark = $false; x = $blockW + 20 })
foreach ($t in $themes)
{
    (TY 'Theme').GetField('Dark', $ST).SetValue($null, $t.dark)
    $bg = (TY 'Theme').GetProperty('Panel', $ST).GetValue($null, $null)
    $fg = (TY 'Theme').GetProperty('Text', $ST).GetValue($null, $null)
    $b = New-Object Drawing.SolidBrush $bg
    $g.FillRectangle($b, $t.x, 0, $blockW, $blockH); $b.Dispose()
    [void]$smooth.Invoke($null, @($g))
    for ($si = 0; $si -lt $sizes.Count; $si++)
    {
        $px = [float][Math]::Round($sizes[$si] * $Scale)
        for ($i = 0; $i -lt $names.Count; $i++)
        {
            $cx = $t.x + ($i % $cols) * $cell
            $cy = ($si * $rows + [Math]::Floor($i / $cols)) * $cell
            $box = New-Object Drawing.RectangleF ([float]($cx + ($cell - $px) / 2)), ([float]($cy + ($cell - $px) / 2)), $px, $px
            $ico = [Enum]::Parse($icoT, $names[$i])
            [void]$draw.Invoke($null, @($g, $ico, $box, $fg, [float]1.9))
        }
    }
}
$g.Dispose()
$dir = Join-Path $env:TEMP 'ss-shots'
New-Item -ItemType Directory -Force $dir | Out-Null
$out = Join-Path $dir ('icons-' + $Tag + '.png')
$bmp.Save($out, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Host ('{0} icons x {1} sizes -> {2}' -f $names.Count, $sizes.Count, $out)
Write-Host ('order: ' + ($names -join ' '))
