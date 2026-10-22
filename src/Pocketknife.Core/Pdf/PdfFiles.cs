using Pocketknife.Core.Running;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Writer;

namespace Pocketknife.Core.Pdf;

public static class PdfFiles
{
    /// <summary>Opens a PDF, translating library errors into readable messages.</summary>
    public static PdfDocument Open(string path)
    {
        try
        {
            return PdfDocument.Open(path);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new ToolException($"{Path.GetFileName(path)} is password protected. Remove the password first.", ex);
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            throw new ToolException($"{Path.GetFileName(path)} could not be read as a PDF: {ex.Message}", ex);
        }
    }

    /// <summary>Writes a new PDF holding the given pages, without the source documents' metadata.</summary>
    public static void WritePages(string path, IEnumerable<(PdfDocument Source, int PageNumber)> pages)
    {
        using var builder = new PdfDocumentBuilder { IncludeDocumentInformation = false };
        foreach (var (source, pageNumber) in pages)
        {
            builder.AddPage(source, pageNumber);
        }

        File.WriteAllBytes(path, builder.Build());
    }
}
