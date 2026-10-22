using Pocketknife.Core.Pdf;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Tools.Documents;

public sealed class SplitPdfTool : Tool
{
    public override string Id => "split-pdf";

    public override string Name => "Split PDF";

    public override string Summary => "Break a PDF into single pages, equal parts or the page ranges you list.";

    public override ToolCategory Category => ToolCategory.Documents;

    public override string Glyph => "\uE8C6";

    public override IReadOnlyList<string> Keywords => ["separate", "extract pages", "ranges", "pdf"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Pdf, "PDFs");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("mode", "Split into",
        [
            new("pages", "One file per page"),
            new("every", "Files of a fixed number of pages"),
            new("ranges", "Page ranges I choose"),
        ], "pages"),
        new NumberOption("every", "Pages per file", 2, 1, 10_000) { VisibleWhen = new("mode", ["every"]) },
        new TextOption("ranges", "Page ranges", "")
        {
            Placeholder = "1-3, 5, 8-",
            Help = "Each range becomes its own file. 8- means page 8 to the end.",
            VisibleWhen = new("mode", ["ranges"]),
        },
    ];

    public override Task RunAsync(ToolJob job, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            using var source = PdfFiles.Open(job.File);
            var count = source.NumberOfPages;
            var ranges = job.Options.Choice("mode") switch
            {
                "every" => PageRanges.Chunks(count, job.Options.WholeNumber("every")),
                "ranges" => PageRanges.Parse(job.Options.Text("ranges"), count),
                _ => PageRanges.Chunks(count, 1),
            };

            var stem = Path.GetFileNameWithoutExtension(job.File);
            for (var i = 0; i < ranges.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var range = ranges[i];
                PdfFiles.WritePages(
                    job.OutputPath($"{stem} ({range}).pdf"),
                    Enumerable.Range(range.First, range.Last - range.First + 1).Select(n => (source, n)));
                job.Progress.Report((i + 1.0) / ranges.Count);
            }
        },
        cancellationToken);
}
