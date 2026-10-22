using System.Globalization;
using System.Text;
using Toolbox.Core.Running;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Toolbox.Core.Imaging;

public static class QrCodes
{
    public static BitMatrix Encode(string text, string errorCorrection)
    {
        var level = errorCorrection switch
        {
            "L" => ErrorCorrectionLevel.L,
            "Q" => ErrorCorrectionLevel.Q,
            "H" => ErrorCorrectionLevel.H,
            _ => ErrorCorrectionLevel.M,
        };
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = level,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
            [EncodeHintType.MARGIN] = 4,
        };
        try
        {
            return new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        }
        catch (WriterException ex)
        {
            throw new ToolException("This text is too long for a QR code. Try a lower error correction level or shorter text.", ex);
        }
    }

    /// <summary>Renders whole pixels per module, so the image is at most <paramref name="size"/> pixels wide.</summary>
    public static BgraImage ToImage(BitMatrix matrix, int size)
    {
        var scale = Math.Max(1, size / matrix.Width);
        var width = matrix.Width * scale;
        var pixels = new byte[width * width * 4];
        Array.Fill(pixels, (byte)255);
        var image = new BgraImage(width, width, pixels);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y])
                {
                    image.FillRectangle(new System.Windows.Int32Rect(x * scale, y * scale, scale, scale), 0, 0, 0);
                }
            }
        }

        return image;
    }

    public static string ToSvg(BitMatrix matrix)
    {
        var path = new StringBuilder();
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (!matrix[x, y])
                {
                    continue;
                }

                var run = 1;
                while (x + run < matrix.Width && matrix[x + run, y])
                {
                    run++;
                }

                path.Append(CultureInfo.InvariantCulture, $"M{x} {y}h{run}v1h-{run}z");
                x += run - 1;
            }
        }

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {matrix.Width} {matrix.Height}" shape-rendering="crispEdges">
              <rect width="100%" height="100%" fill="#ffffff"/>
              <path fill="#000000" d="{path}"/>
            </svg>

            """;
    }

    public static IReadOnlyList<string> Decode(BgraImage image)
    {
        var flattened = new byte[image.Pixels.Length];
        for (var i = 0; i < flattened.Length; i += 4)
        {
            var alpha = image.Pixels[i + 3] / 255f;
            for (var c = 0; c < 3; c++)
            {
                flattened[i + c] = (byte)((image.Pixels[i + c] * alpha) + (255 * (1 - alpha)));
            }

            flattened[i + 3] = 255;
        }

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
                CharacterSet = "UTF-8",
            },
        };
        var source = new RGBLuminanceSource(flattened, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(source) ?? [];
        return [.. results.Select(r => r.Text).Distinct()];
    }
}
