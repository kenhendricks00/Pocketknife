<#
.SYNOPSIS
Regenerates the small test fixtures in tests/Pocketknife.Tests/Fixtures.
Needs ffmpeg on PATH (or pass -FFmpeg). Everything is synthetic: test patterns, a sine tone,
Windows text-to-speech and a hand-written PDF.
#>
param(
    [string]$FFmpeg = "ffmpeg"
)

$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "..\tests\Pocketknife.Tests\Fixtures"
New-Item -ItemType Directory -Force $out | Out-Null

# Video: 2 s test pattern with a tone, encoded at high quality so compression has something to remove.
& $FFmpeg -hide_banner -loglevel error -y `
    -f lavfi -i "testsrc2=size=320x240:rate=25:duration=2" `
    -f lavfi -i "sine=frequency=440:duration=2" `
    -c:v libx264 -crf 14 -pix_fmt yuv420p -c:a aac -b:a 96k -shortest (Join-Path $out "sample.mp4")

# Speech: Windows text-to-speech, stored as small mono MP3.
Add-Type -AssemblyName System.Speech
$wav = Join-Path ([IO.Path]::GetTempPath()) "pocketknife-speech.wav"
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Rate = -1
$synth.SetOutputToWaveFile($wav)
$synth.Speak("The quick brown fox jumps over the lazy dog. Everything runs on this computer.")
$synth.Dispose()
& $FFmpeg -hide_banner -loglevel error -y -i $wav -ac 1 -ar 16000 -c:a libmp3lame -b:a 32k (Join-Path $out "speech.mp3")
Remove-Item $wav

# Photo-like subject for background removal: a shaded orange ball with a shadow on a soft gray backdrop.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 400, 300
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = "AntiAlias"
$backdrop = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(0, 300)), ([System.Drawing.Color]::FromArgb(236, 238, 241)), ([System.Drawing.Color]::FromArgb(206, 210, 216))
$g.FillRectangle($backdrop, 0, 0, 400, 300)
$shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 40, 40, 50))
$g.FillEllipse($shadow, 140, 232, 130, 26)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddEllipse(130, 70, 140, 140)
$ball = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
$ball.CenterPoint = [System.Drawing.PointF]::new(175, 110)
$ball.CenterColor = [System.Drawing.Color]::FromArgb(255, 214, 120)
$ball.SurroundColors = @([System.Drawing.Color]::FromArgb(200, 70, 10))
$g.FillPath($ball, $path)
$g.Dispose()
$bitmap.Save((Join-Path $out "subject.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

# PDF: three pages of Helvetica text with document metadata, written by hand so the bytes are fully known.
$pages = @(
    @("Invoice for Jane Doe", "Contact: jane.doe@example.com", "Phone: (555) 123-4567", "Account number 4111-2222-3333"),
    @("Page two", "Nothing secret here."),
    @("Page three", "Signed by Jane Doe.")
)
$objects = New-Object System.Collections.Generic.List[string]
$objects.Add("<< /Type /Catalog /Pages 2 0 R >>")
$kids = (0..($pages.Count - 1) | ForEach-Object { "$(3 + $_) 0 R" }) -join " "
$objects.Add("<< /Type /Pages /Kids [$kids] /Count $($pages.Count) >>")
$fontId = 3 + 2 * $pages.Count
for ($i = 0; $i -lt $pages.Count; $i++) {
    $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents $(3 + $pages.Count + $i) 0 R /Resources << /Font << /F1 $fontId 0 R >> >> >>")
}
foreach ($lines in $pages) {
    $y = 700
    $content = ($lines | ForEach-Object { $line = "BT /F1 18 Tf 72 $y Td ($_) Tj ET"; $y -= 36; $line }) -join "`n"
    $objects.Add("<< /Length $($content.Length) >>`nstream`n$content`nendstream")
}
$objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
$objects.Add("<< /Title (Confidential report) /Author (Jane Doe) /Subject (Quarterly secrets) /Keywords (private) >>")
$infoId = $objects.Count

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("%PDF-1.4`n")
$offsets = @()
for ($i = 0; $i -lt $objects.Count; $i++) {
    $offsets += $sb.Length
    [void]$sb.Append("$($i + 1) 0 obj`n$($objects[$i])`nendobj`n")
}
$xref = $sb.Length
[void]$sb.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
foreach ($offset in $offsets) { [void]$sb.Append(("{0:D10} 00000 n `n" -f $offset)) }
[void]$sb.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R /Info $infoId 0 R >>`nstartxref`n$xref`n%%EOF`n")
[IO.File]::WriteAllText((Join-Path $out "sample.pdf"), $sb.ToString(), [Text.Encoding]::ASCII)

Get-ChildItem $out | Format-Table Name, Length
