<#
.SYNOPSIS
Renders the app icon (the design in assets/icon.svg) to src/Pocketknife.App/Assets/app.ico and assets/logo.png.
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = "AntiAlias"
    $g.TextRenderingHint = "AntiAliasGridFit"
    $radius = $size * 14 / 64
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $radius, 2 * $radius, 180, 90)
    $path.AddArc($size - 2 * $radius - 1, 0, 2 * $radius, 2 * $radius, 270, 90)
    $path.AddArc($size - 2 * $radius - 1, $size - 2 * $radius - 1, 2 * $radius, 2 * $radius, 0, 90)
    $path.AddArc(0, $size - 2 * $radius - 1, 2 * $radius, 2 * $radius, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#16a34a"))), $path)
    $font = New-Object System.Drawing.Font "Segoe UI Semibold", ($size * 26 / 64), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = "Center"
    $format.LineAlignment = "Center"
    $g.DrawString("PK", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ($size * 0.02), $size, $size), $format)
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

$root = Join-Path $PSScriptRoot ".."
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (New-IconPng $_) }

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $writer.Write([byte]($s % 256)); $writer.Write([byte]($s % 256)); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

$assets = Join-Path $root "src\Pocketknife.App\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null
[IO.File]::WriteAllBytes((Join-Path $assets "app.ico"), $ico.ToArray())
[IO.File]::WriteAllBytes((Join-Path $root "assets\logo.png"), $images[-1])
