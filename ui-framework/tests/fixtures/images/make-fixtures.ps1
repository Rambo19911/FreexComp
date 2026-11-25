# SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
# One-off generator of the fixture images of this folder (see README.md). Run by hand with Windows PowerShell;
# never run by a test. Every picture is own content: flat-colour blocks. Nothing is downloaded.
# The PNG, GIF and JPEG files are written by the imaging classes of the operating system; progressive.jpg is
# written by make-progressive.py (the system encoder has no progressive mode) and plain.bmp by MakePlainBmp.pas.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Four blocks (top left, top right, bottom left, bottom right), each colour given as blue, green, red, alpha.
function New-Blocks([int]$w, [int]$h, [byte[][]]$colours) {
    $pixels = New-Object byte[] ($w * $h * 4)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $i = 0
            if ($x -ge $w / 2) { $i += 1 }
            if ($y -ge $h / 2) { $i += 2 }
            [Array]::Copy($colours[$i], 0, $pixels, ($y * $w + $x) * 4, 4)
        }
    }
    return [System.Windows.Media.Imaging.BitmapSource]::Create($w, $h, 96, 96,
        [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, $w * 4)
}

function Save-Image($encoder, $frame, [string]$name) {
    $encoder.Frames.Add($frame)
    $stream = [System.IO.File]::Create((Join-Path $here $name))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

function New-Frame($source, $metadata) {
    return [System.Windows.Media.Imaging.BitmapFrame]::Create($source, $null, $metadata, $null)
}

# alpha.png: opaque red, opaque green, half-transparent blue (alpha 128), fully transparent
$alpha = New-Blocks 16 12 @((0, 0, 255, 255), (0, 255, 0, 255), (255, 0, 0, 128), (0, 0, 0, 0))
Save-Image (New-Object System.Windows.Media.Imaging.PngBitmapEncoder) (New-Frame $alpha $null) 'alpha.png'

# blocks.gif: red, green, blue, yellow through a palette of exactly these four colours
$palette = New-Object System.Windows.Media.Imaging.BitmapPalette (, [System.Windows.Media.Color[]]@(
    [System.Windows.Media.Color]::FromRgb(255, 0, 0), [System.Windows.Media.Color]::FromRgb(0, 255, 0),
    [System.Windows.Media.Color]::FromRgb(0, 0, 255), [System.Windows.Media.Color]::FromRgb(255, 255, 0)))
$index = New-Object byte[] (16 * 12)
for ($y = 0; $y -lt 12; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
        $i = 0
        if ($x -ge 8) { $i += 1 }
        if ($y -ge 6) { $i += 2 }
        $index[$y * 16 + $x] = $i
    }
}
$gif = [System.Windows.Media.Imaging.BitmapSource]::Create(16, 12, 96, 96,
    [System.Windows.Media.PixelFormats]::Indexed8, $palette, $index, 16)
Save-Image (New-Object System.Windows.Media.Imaging.GifBitmapEncoder) (New-Frame $gif $null) 'blocks.gif'

# baseline.jpg and rotated.jpg: 32 x 24, four blocks; rotated.jpg carries the orientation value 6
$photo = New-Blocks 32 24 @((40, 40, 200, 255), (60, 160, 40, 255), (200, 70, 50, 255), (80, 220, 230, 255))
$photo = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap ($photo,
    [System.Windows.Media.PixelFormats]::Bgr24, $null, 0)
$jpeg = New-Object System.Windows.Media.Imaging.JpegBitmapEncoder
$jpeg.QualityLevel = 95
Save-Image $jpeg (New-Frame $photo $null) 'baseline.jpg'

$metadata = New-Object System.Windows.Media.Imaging.BitmapMetadata 'jpg'
$metadata.SetQuery('/app1/ifd/{ushort=274}', [uint16]6)
$jpeg = New-Object System.Windows.Media.Imaging.JpegBitmapEncoder
$jpeg.QualityLevel = 95
Save-Image $jpeg (New-Frame $photo $metadata) 'rotated.jpg'

# broken.png: alpha.png cut after 40 bytes; not-an-image.png: 64 bytes of text
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $here 'alpha.png'))
$cut = New-Object byte[] 40
[Array]::Copy($bytes, $cut, 40)
[System.IO.File]::WriteAllBytes((Join-Path $here 'broken.png'), $cut)
$text = 'This file is text, not an image. It is exactly 64 bytes long....'
if ($text.Length -ne 64) { throw 'the text must be 64 bytes long' }
[System.IO.File]::WriteAllBytes((Join-Path $here 'not-an-image.png'), [System.Text.Encoding]::ASCII.GetBytes($text))
