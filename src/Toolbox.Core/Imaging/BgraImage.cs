using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Core.Running;

namespace Toolbox.Core.Imaging;

/// <summary>A decoded image as straight (non-premultiplied) BGRA bytes, using the Windows Imaging Component.</summary>
public sealed class BgraImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public int Stride => Width * 4;

    public byte[] Pixels { get; } = pixels;

    public static BgraImage Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            throw new ToolException("This image could not be read. It may be damaged or in a format Windows can't open.", ex);
        }
    }

    public static BgraImage Load(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        BitmapSource source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        source = ApplyOrientation(source, frame.Metadata as BitmapMetadata);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return new BgraImage(source.PixelWidth, source.PixelHeight, pixels);
    }

    public BitmapSource ToBitmapSource()
    {
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public void SavePng(string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource()));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public byte[] EncodeJpeg(int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(ToBitmapSource(), PixelFormats.Bgr24, null, 0)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public (byte B, byte G, byte R, byte A) GetPixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    /// <summary>Fills a rectangle (clipped to the image) with an opaque color.</summary>
    public void FillRectangle(Int32Rect rect, byte b, byte g, byte r)
    {
        var x0 = Math.Clamp(rect.X, 0, Width);
        var y0 = Math.Clamp(rect.Y, 0, Height);
        var x1 = Math.Clamp(rect.X + rect.Width, 0, Width);
        var y1 = Math.Clamp(rect.Y + rect.Height, 0, Height);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = (y * Width + x) * 4;
                Pixels[i] = b;
                Pixels[i + 1] = g;
                Pixels[i + 2] = r;
                Pixels[i + 3] = 255;
            }
        }
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, BitmapMetadata? metadata)
    {
        ushort orientation = 1;
        try
        {
            if (metadata?.GetQuery("System.Photo.Orientation") is ushort value)
            {
                orientation = value;
            }
        }
        catch (NotSupportedException)
        {
        }

        if (orientation is < 2 or > 8)
        {
            return source;
        }

        var transform = new TransformGroup();
        if (orientation is 2 or 4 or 5 or 7)
        {
            transform.Children.Add(new ScaleTransform(-1, 1));
        }

        var angle = orientation switch
        {
            3 or 4 => 180,
            5 or 6 => 90,
            7 or 8 => 270,
            _ => 0,
        };
        if (angle != 0)
        {
            transform.Children.Add(new RotateTransform(angle));
        }

        return new TransformedBitmap(source, transform);
    }
}
