<#
.SYNOPSIS
Builds every logo asset from the Pocketknife artwork.

.DESCRIPTION
With -Source, the artwork is cleaned (stray transparent specks removed), cropped and centered on a square
transparent canvas, and saved as the master assets/logo.png. Without -Source, the existing master is used.

From the master it writes:
  src/Pocketknife.App/Assets/app.ico   exe, window, tray and installer icon (256, 64, 48, 40, 32, 24, 20, 16 px)
  assets/banner.png                     README header: logo and wordmark (900x298, shown at 450x149)
  assets/OG.png                         1280x640 social preview over a blurred app screenshot
  installer/wizard-small-*.png          installer wizard images

Sizes of 32 px and below fill the canvas (1 px margin), are lightly sharpened, and get slightly deeper metal
tones plus a faint 1 px contour so the silver blade stays legible on light taskbars.
#>
param(
    [string]$Source
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class LogoKit
{
    static byte[] Read(Bitmap b, out int w, out int h)
    {
        w = b.Width; h = b.Height;
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[w * h * 4];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        b.UnlockBits(data);
        return px;
    }

    static Bitmap Write(byte[] px, int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        b.UnlockBits(data);
        return b;
    }

    // Keeps only the largest 8-connected shape and clears color under fully transparent pixels.
    public static Bitmap Clean(Bitmap source)
    {
        int w, h;
        var px = Read(source, out w, out h);
        var label = new int[w * h];
        int best = 0, bestSize = 0, next = 0;
        var stack = new Stack<int>();
        for (int i = 0; i < w * h; i++)
        {
            if (px[i * 4 + 3] == 0 || label[i] != 0) continue;
            next++;
            int size = 0;
            stack.Push(i); label[i] = next;
            while (stack.Count > 0)
            {
                int p = stack.Pop(); size++;
                int x = p % w, y = p / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int q = ny * w + nx;
                    if (label[q] == 0 && px[q * 4 + 3] != 0) { label[q] = next; stack.Push(q); }
                }
            }
            if (size > bestSize) { bestSize = size; best = next; }
        }
        for (int i = 0; i < w * h; i++)
        {
            if (label[i] != best) px[i * 4 + 3] = 0;
            if (px[i * 4 + 3] == 0) { px[i * 4] = 0; px[i * 4 + 1] = 0; px[i * 4 + 2] = 0; }
        }
        return Write(px, w, h);
    }

    public static Rectangle Bounds(Bitmap b, byte threshold)
    {
        int w, h;
        var px = Read(b, out w, out h);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (px[(y * w + x) * 4 + 3] <= threshold) continue;
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    // Centers the crop of source on a transparent square canvas, scaled to fit inside size minus margin.
    public static Bitmap Square(Bitmap source, Rectangle crop, int size, double margin)
    {
        var current = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(current)) g.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
        double inner = size * (1 - 2 * margin);
        double scale = Math.Min(inner / crop.Width, inner / crop.Height);
        int tw = Math.Max(1, (int)Math.Round(crop.Width * scale)), th = Math.Max(1, (int)Math.Round(crop.Height * scale));
        while (current.Width / 2 >= tw && current.Height / 2 >= th)
        {
            current = Resize(current, current.Width / 2, current.Height / 2);
        }
        var fitted = Resize(current, tw, th);
        var canvas = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(fitted, (size - tw) / 2, (size - th) / 2);
        }
        return ToArgb(canvas);
    }

    static Bitmap Resize(Bitmap source, int w, int h)
    {
        var result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(result))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        return result;
    }

    static Bitmap ToArgb(Bitmap b)
    {
        var result = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(b, 0, 0); }
        return result;
    }

    // Unsharp mask on premultiplied color, with a gentler pass on alpha to firm up edges.
    public static Bitmap Sharpen(Bitmap b, double amount, double alphaAmount)
    {
        int w, h;
        var px = Read(b, out w, out h);
        var pre = new double[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            double a = px[i * 4 + 3] / 255.0;
            for (int c = 0; c < 3; c++) pre[i * 4 + c] = px[i * 4 + c] * a;
            pre[i * 4 + 3] = px[i * 4 + 3];
        }
        int[] k = { 1, 2, 1, 2, 4, 2, 1, 2, 1 };
        var outPx = new byte[px.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var blur = new double[4];
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int sx = Math.Min(w - 1, Math.Max(0, x + dx)), sy = Math.Min(h - 1, Math.Max(0, y + dy));
                int weight = k[(dy + 1) * 3 + dx + 1];
                for (int c = 0; c < 4; c++) blur[c] += pre[(sy * w + sx) * 4 + c] * weight;
            }
            int i = y * w + x;
            double alpha = pre[i * 4 + 3] + alphaAmount * (pre[i * 4 + 3] - blur[3] / 16);
            alpha = Math.Min(255, Math.Max(0, alpha));
            outPx[i * 4 + 3] = (byte)Math.Round(alpha);
            for (int c = 0; c < 3; c++)
            {
                double v = pre[i * 4 + c] + amount * (pre[i * 4 + c] - blur[c] / 16);
                double straight = alpha > 0 ? v / (alpha / 255.0) : 0;
                outPx[i * 4 + c] = (byte)Math.Round(Math.Min(255, Math.Max(0, straight)));
            }
        }
        return Write(outPx, w, h);
    }

    // For tiny sizes: deepens the bright, unsaturated metal and adds a faint 1 px dark contour behind the shape,
    // so the silver blade still reads against light taskbars without changing the look on dark ones.
    public static Bitmap SmallSizeContrast(Bitmap b, double metal, int outlineAlpha)
    {
        int w, h;
        var px = Read(b, out w, out h);
        var outPx = new byte[px.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            double r = px[i + 2], g = px[i + 1], bl = px[i];
            double max = Math.Max(r, Math.Max(g, bl)), min = Math.Min(r, Math.Min(g, bl));
            double saturation = max > 0 ? (max - min) / max : 0;
            double k = saturation < 0.25 ? metal : 1;
            double a = px[i + 3] / 255.0;
            int around = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h) around = Math.Max(around, px[(ny * w + nx) * 4 + 3]);
            }
            double ring = around / 255.0 * outlineAlpha / 255.0;
            double outA = a + ring * (1 - a);
            double[] fg = { bl * k, g * k, r * k };
            double[] edge = { 48, 40, 40 };
            for (int c = 0; c < 3; c++)
            {
                double v = outA > 0 ? (fg[c] * a + edge[c] * ring * (1 - a)) / outA : 0;
                outPx[i + c] = (byte)Math.Round(Math.Min(255, Math.Max(0, v)));
            }
            outPx[i + 3] = (byte)Math.Round(outA * 255);
        }
        return Write(outPx, w, h);
    }

    public static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // 32-bit BMP icon entry: bottom-up straight BGRA plus an AND mask for transparent pixels.
    public static byte[] IconBmp(Bitmap b)
    {
        int w, h;
        var px = Read(b, out w, out h);
        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--) bw.Write(px, y * w * 4, w * 4);
            for (int y = h - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < w; x++) if (px[(y * w + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                bw.Write(row);
            }
            return ms.ToArray();
        }
    }

    public static byte[] Ico(int[] sizes, byte[][] images)
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)(sizes[i] % 256)); bw.Write((byte)(sizes[i] % 256)); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32); bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) bw.Write(image);
            return ms.ToArray();
        }
    }

    public static Bitmap Blur(Bitmap b, int factor)
    {
        var small = Resize(b, Math.Max(1, b.Width / factor), Math.Max(1, b.Height / factor));
        return ToArgb(Resize(small, b.Width, b.Height));
    }
}
"@

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$assets = Join-Path $root "assets"
$master = Join-Path $assets "logo.png"

function Save-Png($bitmap, $path) {
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("{0,8:N0} bytes  {1}" -f (Get-Item $path).Length, $path.Substring($root.Path.Length + 1))
}

if ($Source) {
    $raw = New-Object System.Drawing.Bitmap $Source
    $clean = [LogoKit]::Clean($raw)
    $crop = [LogoKit]::Bounds($clean, 0)
    $side = [Math]::Max($crop.Width, $crop.Height)
    $squared = [LogoKit]::Square($clean, $crop, [int][Math]::Round($side / 0.92), 0.04)
    $raw.Dispose()
    Save-Png $squared $master
    $squared.Dispose()
}

$logo = New-Object System.Drawing.Bitmap $master
$art = [LogoKit]::Bounds($logo, 8)

$sizes = 256, 64, 48, 40, 32, 24, 20, 16
$preview = Join-Path ([IO.Path]::GetTempPath()) "pocketknife-icon-sizes"
New-Item -ItemType Directory -Force $preview | Out-Null
$entries = foreach ($size in $sizes) {
    if ($size -ge 48) {
        $icon = [LogoKit]::Square($logo, $art, $size, 0.03)
    }
    else {
        $fitted = [LogoKit]::Sharpen([LogoKit]::Square($logo, $art, $size, 1.0 / $size), 0.55, 0.25)
        $icon = [LogoKit]::SmallSizeContrast($fitted, 0.86, 110)
    }
    $icon.Save((Join-Path $preview "$size.png"))
    if ($size -eq 256) { , [LogoKit]::Png($icon) } else { , [LogoKit]::IconBmp($icon) }
}
$ico = Join-Path $root "src\Pocketknife.App\Assets\app.ico"
[IO.File]::WriteAllBytes($ico, [LogoKit]::Ico($sizes, $entries))
Write-Host ("{0,8:N0} bytes  {1}" -f (Get-Item $ico).Length, $ico.Substring($root.Path.Length + 1))

foreach ($size in 55, 110) {
    Save-Png ([LogoKit]::Square($logo, $art, $size, 0.04)) (Join-Path $root "installer\wizard-small-$size.png")
}

$brand = [System.Drawing.ColorTranslator]::FromHtml("#D3172B")
$banner = New-Object System.Drawing.Bitmap 900, 298
$g = [System.Drawing.Graphics]::FromImage($banner)
$g.SmoothingMode = "AntiAlias"; $g.TextRenderingHint = "AntiAliasGridFit"; $g.InterpolationMode = "HighQualityBicubic"
$g.DrawImage([LogoKit]::Square($logo, $art, 270, 0.0), 14, 14, 270, 270)
$format = New-Object System.Drawing.StringFormat ([System.Drawing.StringFormatFlags]::NoWrap)
$format.LineAlignment = "Center"
for ($px = 118; ; $px -= 2) {
    $font = New-Object System.Drawing.Font "Segoe UI", $px, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    if ($g.MeasureString("Pocketknife", $font, 10000, $format).Width -le 596) { break }
}
$g.DrawString("Pocketknife", $font, (New-Object System.Drawing.SolidBrush $brand), (New-Object System.Drawing.RectangleF 296, 0, 604, 298), $format)
$g.Dispose()
Save-Png $banner (Join-Path $assets "banner.png")

$og = New-Object System.Drawing.Bitmap 1280, 640
$g = [System.Drawing.Graphics]::FromImage($og)
$g.SmoothingMode = "AntiAlias"; $g.TextRenderingHint = "AntiAliasGridFit"; $g.InterpolationMode = "HighQualityBicubic"
$backdrop = New-Object System.Drawing.Bitmap (Join-Path $assets "screenshots\home-dark.png")
$scale = [Math]::Max(1280 / $backdrop.Width, 640 / $backdrop.Height)
$covered = New-Object System.Drawing.Bitmap 1280, 640
$cg = [System.Drawing.Graphics]::FromImage($covered)
$cg.InterpolationMode = "HighQualityBicubic"
$cg.DrawImage($backdrop, (1280 - $backdrop.Width * $scale) / 2, (640 - $backdrop.Height * $scale) / 2, $backdrop.Width * $scale, $backdrop.Height * $scale)
$cg.Dispose()
$g.DrawImage([LogoKit]::Blur($covered, 24), 0, 0, 1280, 640)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(115, 12, 12, 16))), 0, 0, 1280, 640)
$g.DrawImage([LogoKit]::Square($logo, $art, 300, 0.0), 150, 170, 300, 300)
$font = New-Object System.Drawing.Font "Segoe UI", 112, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$small = New-Object System.Drawing.Font "Segoe UI", 40, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString("Pocketknife", $font, [System.Drawing.Brushes]::White, 472, 222)
$g.DrawString("A PC multitool for Windows", $small, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 255, 255, 255))), 484, 368)
$g.Dispose()
Save-Png $og (Join-Path $assets "OG.png")
