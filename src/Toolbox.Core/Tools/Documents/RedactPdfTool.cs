using System.Globalization;
using System.Windows;
using Toolbox.Core.Pdf;
using Toolbox.Core.Running;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Toolbox.Core.Tools.Documents;

/// <summary>
/// True redaction: every page is rendered to an image with the matches painted black, and a new PDF is built
/// from those images. No text layer, hidden objects, annotations or metadata from the original survive.
/// </summary>
public sealed class RedactPdfTool : Tool
{
    public override string Id => "redact-pdf";

    public override string Name => "Redact PDF";

    public override string Summary => "Black out words, emails and phone numbers for good. Pages are flattened so the text underneath is gone.";

    public override ToolCategory Category => ToolCategory.Documents;

    public override string Glyph => "\uE72E";

    public override IReadOnlyList<string> Keywords => ["black out", "censor", "hide", "privacy", "remove text", "metadata", "pdf"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Pdf, "PDFs");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new TextOption("terms", "Words or phrases to redact", "")
        {
            Multiline = true,
            Placeholder = "One per line, for example a name or account number",
        },
        new ToggleOption("emails", "Redact email addresses", false),
        new ToggleOption("phones", "Redact phone numbers", false),
        new ToggleOption("matchCase", "Match case", false),
        new ChoiceOption("dpi", "Page quality",
        [
            new("150", "Standard (150 dpi)"),
            new("200", "High (200 dpi)"),
            new("300", "Print (300 dpi)"),
        ], "200") { Help = "Redacted pages become images, so the output can't be searched or copied from." },
    ];

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        var pattern = RedactionFinder.BuildPattern(
            job.Options.Text("terms").Split('\n'),
            job.Options.Toggle("emails"),
            job.Options.Toggle("phones"),
            job.Options.Toggle("matchCase"))
            ?? throw new ToolException("Enter words to redact, or turn on email addresses or phone numbers.");
        var dpi = double.Parse(job.Options.Choice("dpi"), CultureInfo.InvariantCulture);

        using var rasterizer = await PdfRasterizer.OpenAsync(job.File);
        using var text = PdfFiles.Open(job.File);
        using var output = new PdfDocumentBuilder { IncludeDocumentInformation = false };
        var matches = 0;
        var pagesWithMatches = 0;
        for (var i = 0; i < rasterizer.PageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var areas = RedactionFinder.Find(text.GetPage(i + 1), pattern);
            matches += areas.Count;
            pagesWithMatches += areas.Count > 0 ? 1 : 0;

            var rendered = await rasterizer.RenderAsync(i, dpi);
            var scale = rendered.Image.Width / rendered.WidthPoints;
            foreach (var area in areas)
            {
                rendered.Image.FillRectangle(
                    new Int32Rect(
                        (int)Math.Floor(area.X * scale),
                        (int)Math.Floor(area.Y * scale),
                        (int)Math.Ceiling(area.Width * scale) + 1,
                        (int)Math.Ceiling(area.Height * scale) + 1),
                    0, 0, 0);
            }

            var page = output.AddPage(rendered.WidthPoints, rendered.HeightPoints);
            page.AddJpeg(rendered.Image.EncodeJpeg(92), new PdfRectangle(0, 0, rendered.WidthPoints, rendered.HeightPoints));
            job.Progress.Report((i + 1.0) / rasterizer.PageCount);
        }

        await File.WriteAllBytesAsync(job.OutputPathFor(job.File, ".pdf", " (redacted)"), output.Build(), cancellationToken);

        job.Report(matches == 0
            ? $"{Path.GetFileName(job.File)}: nothing matched. Pages were still flattened and metadata removed."
            : $"{Path.GetFileName(job.File)}: redacted {matches} {(matches == 1 ? "match" : "matches")} on {pagesWithMatches} {(pagesWithMatches == 1 ? "page" : "pages")}.");
    }
}
