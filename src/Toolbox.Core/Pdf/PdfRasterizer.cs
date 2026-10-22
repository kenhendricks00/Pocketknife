using Toolbox.Core.Imaging;
using Toolbox.Core.Running;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace Toolbox.Core.Pdf;

public sealed record RenderedPage(BgraImage Image, double WidthPoints, double HeightPoints);

/// <summary>Renders PDF pages to pixels with the PDF engine built into Windows (Windows.Data.Pdf).</summary>
public sealed class PdfRasterizer : IDisposable
{
    private readonly FileStream _file;
    private readonly WinPdfDocument _document;

    private PdfRasterizer(FileStream file, WinPdfDocument document)
    {
        _file = file;
        _document = document;
    }

    public int PageCount => (int)_document.PageCount;

    public static async Task<PdfRasterizer> OpenAsync(string path)
    {
        var file = File.OpenRead(path);
        try
        {
            var document = await WinPdfDocument.LoadFromStreamAsync(file.AsRandomAccessStream());
            return new PdfRasterizer(file, document);
        }
        catch (Exception ex)
        {
            await file.DisposeAsync();
            throw new ToolException($"{Path.GetFileName(path)} could not be opened. It may be damaged or password protected.", ex);
        }
    }

    public async Task<RenderedPage> RenderAsync(int pageIndex, double dpi)
    {
        using var page = _document.GetPage((uint)pageIndex);
        var size = page.Size;
        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(size.Width * dpi / 96)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(size.Height * dpi / 96)),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
        });
        output.Seek(0);
        using var stream = output.AsStreamForRead();
        return new RenderedPage(BgraImage.Load(stream), size.Width * 72 / 96, size.Height * 72 / 96);
    }

    public void Dispose() => _file.Dispose();
}
