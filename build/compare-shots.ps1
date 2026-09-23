# compare-shots.ps1 - the same region from two screenshots, enlarged, one above the other.
# For judging line quality: a half-pixel smear is invisible at 100% and obvious at 3x.
#   compare-shots.ps1 -A before.png -B after.png -X 0 -Y 40 -W 760 -H 70 -Scale 2 -Out cmp.png
param(
    [Parameter(Mandatory = $true)][string]$A,
    [Parameter(Mandatory = $true)][string]$B,
    [int]$X = 0, [int]$Y = 0, [int]$W = 400, [int]$H = 100,
    [int]$Scale = 3,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ia = [Drawing.Image]::FromFile((Resolve-Path $A))
$ib = [Drawing.Image]::FromFile((Resolve-Path $B))
$gap = 6
$ow = $W * $Scale
$oh = $H * $Scale * 2 + $gap
$bmp = New-Object Drawing.Bitmap $ow, $oh
$g = [Drawing.Graphics]::FromImage($bmp)
$g.Clear([Drawing.Color]::FromArgb(255, 255, 0, 170))       # a loud divider, never mistaken for UI
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
$src = New-Object Drawing.Rectangle $X, $Y, $W, $H
$g.DrawImage($ia, (New-Object Drawing.Rectangle 0, 0, $ow, ($H * $Scale)), $src, [Drawing.GraphicsUnit]::Pixel)
$g.DrawImage($ib, (New-Object Drawing.Rectangle 0, ($H * $Scale + $gap), $ow, ($H * $Scale)), $src, [Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose(); $ia.Dispose(); $ib.Dispose()
Write-Host ('-> ' + $Out + '  (top: A, bottom: B)')
