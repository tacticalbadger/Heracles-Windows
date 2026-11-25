<#
.SYNOPSIS
    Draws the app icon again, after a change to app\Heracles\Icon.

.DESCRIPTION
        tools\make-icon.ps1 [-Sheet <png>]

    The icon's source is the Mac app's (macos/Icon/Heracles.icon there): a
    page, what is written on it in ink, the lines of text and the tick, as
    SVG layers, and icon.json, which names the two colours of the
    background. On the Mac, Icon Composer draws them. Windows has nothing of
    the kind, so this script does, with WPF: it reads the rectangles and
    paths out of the four layers and paints them at every size Windows asks
    an icon for.

    It writes, into app\Heracles\Icon:
      Heracles.ico   the icon Windows shows: the program file, the windows,
                     the taskbar. Ten sizes, 16 to 256 pixels
      Heracles.png   a picture of it, for the README and for reminders

    Both are kept in the repository, so building the app does not need this
    script. Below 40 pixels the page is drawn larger on its background and
    without its shadow: at its true size the writing on it is a smudge.

    -Sheet also writes every size side by side, on white and on black, to
    the file named: something to look at.
#>
[CmdletBinding()]
param(
    [string]$Sheet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml

$Root = Split-Path -Parent $PSScriptRoot
$Icon = Join-Path $Root 'app\Heracles\Icon'
$Sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$Canvas = 1024.0
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

function ConvertTo-Number([string]$text) { return [double]::Parse($text, $Invariant) }

function ConvertTo-Brush([string]$colour) {
    $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($colour))
    $brush.Freeze()
    return $brush
}

# "srgb:0.94902,0.64314,0.36863,1.00000", as icon.json writes a colour.
function ConvertTo-Colour([string]$text) {
    $parts = @(($text -replace '^srgb:', '') -split ',' | ForEach-Object { ConvertTo-Number $_ })
    return [System.Windows.Media.Color]::FromArgb(
        [byte][Math]::Round($parts[3] * 255), [byte][Math]::Round($parts[0] * 255),
        [byte][Math]::Round($parts[1] * 255), [byte][Math]::Round($parts[2] * 255))
}

# What one layer holds, as things to paint: each a geometry with a fill, or
# with a pen. Only what the four layers use is understood: rectangles with
# round corners, and paths.
function Read-Layer([string]$name) {
    $svg = [xml][System.IO.File]::ReadAllText((Join-Path $Icon "$name.svg"), [System.Text.Encoding]::UTF8)
    $shapes = @()
    $elements = @($svg.SelectNodes("//*[local-name()='rect' or local-name()='path']"))
    foreach ($element in $elements) {
        # A colour is the element's own, or that of the group it is in.
        $fill = $element.GetAttribute('fill')
        if (-not $fill -and $element.ParentNode.LocalName -eq 'g') { $fill = $element.ParentNode.GetAttribute('fill') }
        if ($element.LocalName -eq 'rect') {
            $rect = New-Object System.Windows.Rect (ConvertTo-Number $element.GetAttribute('x')), (ConvertTo-Number $element.GetAttribute('y')),
                (ConvertTo-Number $element.GetAttribute('width')), (ConvertTo-Number $element.GetAttribute('height'))
            $radius = if ($element.GetAttribute('rx')) { ConvertTo-Number $element.GetAttribute('rx') } else { 0.0 }
            $geometry = New-Object System.Windows.Media.RectangleGeometry $rect, $radius, $radius
        }
        else {
            $geometry = [System.Windows.Media.Geometry]::Parse($element.GetAttribute('d')).Clone()
            if ($element.GetAttribute('fill-rule') -eq 'evenodd') { $geometry.FillRule = [System.Windows.Media.FillRule]::EvenOdd }
            else { $geometry.FillRule = [System.Windows.Media.FillRule]::Nonzero }
        }
        $geometry.Freeze()
        $shape = @{ Geometry = $geometry; Fill = $null; Pen = $null }
        if ($fill -and $fill -ne 'none') { $shape.Fill = ConvertTo-Brush $fill }
        $stroke = $element.GetAttribute('stroke')
        if ($stroke) {
            $pen = New-Object System.Windows.Media.Pen (ConvertTo-Brush $stroke), (ConvertTo-Number $element.GetAttribute('stroke-width'))
            if ($element.GetAttribute('stroke-linecap') -eq 'round') {
                $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
                $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
            }
            if ($element.GetAttribute('stroke-linejoin') -eq 'round') { $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round }
            $pen.Freeze()
            $shape.Pen = $pen
        }
        $shapes += $shape
    }
    if ($shapes.Count -eq 0) { throw "nothing to draw was found in $name.svg" }
    return , $shapes
}

function New-LayerVisual($shapes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    foreach ($shape in $shapes) { $context.DrawGeometry($shape.Fill, $shape.Pen, $shape.Geometry) }
    $context.Close()
    return $visual
}

# The background's two colours, top and bottom: the first fill in icon.json,
# which is the one for the light style. (Windows draws an icon one way,
# whatever the theme.)
$description = [System.IO.File]::ReadAllText((Join-Path $Icon 'icon.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$gradient = @($description.'fill-specializations' | Where-Object { -not $_.PSObject.Properties['appearance'] })[0].value.'linear-gradient'
$Top = ConvertTo-Colour $gradient[0]
$Bottom = ConvertTo-Colour $gradient[1]

$Layers = @{}
foreach ($name in 'page', 'lines', 'ink', 'tick') { $Layers[$name] = Read-Layer $name }

# The icon at one size, as a bitmap with straight (not premultiplied) alpha.
function New-IconBitmap([int]$size) {
    $root = New-Object System.Windows.Media.ContainerVisual
    $root.Transform = New-Object System.Windows.Media.ScaleTransform ($size / $Canvas), ($size / $Canvas)

    # The background: a square with round corners, as the Mac draws its own.
    $background = New-Object System.Windows.Media.DrawingVisual
    $context = $background.RenderOpen()
    $brush = New-Object System.Windows.Media.LinearGradientBrush $Top, $Bottom, 90.0
    $context.DrawRoundedRectangle($brush, $null, (New-Object System.Windows.Rect 0, 0, $Canvas, $Canvas), 232, 232)
    $context.Close()
    $root.Children.Add($background) | Out-Null

    # The page and what is on it, larger where the icon is small.
    $grow = if ($size -le 16) { 1.24 } elseif ($size -le 24) { 1.2 } elseif ($size -le 32) { 1.12 } else { 1.0 }
    $sheet = New-Object System.Windows.Media.ContainerVisual
    $sheet.Transform = New-Object System.Windows.Media.ScaleTransform $grow, $grow, ($Canvas / 2), ($Canvas / 2)
    $page = New-LayerVisual $Layers['page']
    if ($size -ge 40) {
        $shadow = New-Object System.Windows.Media.Effects.DropShadowEffect
        $shadow.Color = [System.Windows.Media.Color]::FromRgb(70, 24, 8)
        $shadow.Opacity = 0.35
        $shadow.Direction = 270
        # In pixels of the picture, whatever the size it is drawn at.
        $shadow.ShadowDepth = [Math]::Max(1.0, $size / 64.0)
        $shadow.BlurRadius = [Math]::Max(2.0, $size / 20.0)
        $page.Effect = $shadow
    }
    $sheet.Children.Add($page) | Out-Null
    foreach ($name in 'lines', 'ink', 'tick') { $sheet.Children.Add((New-LayerVisual $Layers[$name])) | Out-Null }
    $root.Children.Add($sheet) | Out-Null

    $target = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($root)
    $straight = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $target, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
    $straight.Freeze()
    return $straight
}

function Get-Pixels($bitmap) {
    $stride = $bitmap.PixelWidth * 4
    $pixels = New-Object byte[] ($stride * $bitmap.PixelHeight)
    $bitmap.CopyPixels($pixels, $stride, 0)
    return , $pixels
}

function Get-PngBytes($bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

# One picture of an .ico file below 256 pixels: a bitmap header, the pixels
# from the bottom row up, and a mask of one bit a pixel that says which are
# see-through, for whatever still reads it.
function Get-IconBitmapBytes($bitmap) {
    $size = $bitmap.PixelWidth
    $pixels = Get-Pixels $bitmap
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([int]40)
    $writer.Write([int]$size)
    $writer.Write([int]($size * 2))
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]0)
    $writer.Write([int]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($row = $size - 1; $row -ge 0; $row--) { $writer.Write($pixels, $row * $size * 4, $size * 4) }
    for ($row = $size - 1; $row -ge 0; $row--) {
        $line = New-Object byte[] $maskStride
        for ($column = 0; $column -lt $size; $column++) {
            if ($pixels[($row * $size + $column) * 4 + 3] -eq 0) { $line[[int][Math]::Floor($column / 8)] = $line[[int][Math]::Floor($column / 8)] -bor (0x80 -shr ($column % 8)) }
        }
        $writer.Write($line)
    }
    $writer.Flush()
    return , $stream.ToArray()
}

$bitmaps = @{}
foreach ($size in $Sizes) { $bitmaps[$size] = New-IconBitmap $size }

# The .ico: a directory of the pictures, then the pictures. The largest is
# kept as a PNG, as Windows expects of it; the rest as plain bitmaps.
$images = @()
foreach ($size in $Sizes) {
    if ($size -ge 256) { $images += , (Get-PngBytes $bitmaps[$size]) }
    else { $images += , (Get-IconBitmapBytes $bitmaps[$size]) }
}
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$Sizes.Count)
$offset = 6 + 16 * $Sizes.Count
for ($index = 0; $index -lt $Sizes.Count; $index++) {
    $size = $Sizes[$index]
    # 256 is written as 0: the field is one byte.
    $writer.Write([byte]($size % 256)); $writer.Write([byte]($size % 256))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$images[$index].Length)
    $writer.Write([int]$offset)
    $offset += $images[$index].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $Icon 'Heracles.ico'), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $Icon 'Heracles.png'), (Get-PngBytes $bitmaps[256]))
Write-Host "ok: $(Join-Path $Icon 'Heracles.ico') ($($Sizes -join ', ') pixels; $($ico.Length) bytes)"
Write-Host "ok: $(Join-Path $Icon 'Heracles.png')"

if ($Sheet) {
    # Every size in a row, on white and on black.
    $gap = 16
    $width = $gap
    foreach ($size in $Sizes) { $width += $size + $gap }
    $height = 256 + 2 * $gap
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawRectangle([System.Windows.Media.Brushes]::White, $null, (New-Object System.Windows.Rect 0, 0, $width, $height))
    $context.DrawRectangle([System.Windows.Media.Brushes]::Black, $null, (New-Object System.Windows.Rect 0, $height, $width, $height))
    foreach ($band in 0, 1) {
        $x = $gap
        foreach ($size in $Sizes) {
            $context.DrawImage($bitmaps[$size], (New-Object System.Windows.Rect $x, ($band * $height + $gap + 256 - $size), $size, $size))
            $x += $size + $gap
        }
    }
    $context.Close()
    $picture = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $width, ($height * 2), 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $picture.Render($visual)
    $Sheet = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $Sheet))
    [System.IO.File]::WriteAllBytes($Sheet, (Get-PngBytes $picture))
    Write-Host "ok: $Sheet"
}
