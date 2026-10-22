using Pocketknife.Core.Pdf;
using Pocketknife.Core.Running;
using UglyToad.PdfPig;

namespace Pocketknife.Core.Tools.Documents;

public sealed class MergePdfsTool : Tool
{
    public override string Id => "merge-pdfs";

    public override string Name => "Merge PDFs";

    public override string Summary => "Combine several PDFs into one, in the order you choose.";

    public override ToolCategory Category => ToolCategory.Documents;

    public override string Glyph => "\uE8F1";

    public override IReadOnlyList<string> Keywords => ["combine", "join", "append", "pdf"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Pdf, "PDFs", Combine: true, MinimumFiles: 2);

    public override Task RunAsync(ToolJob job, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            var sources = new List<PdfDocument>();
            try
            {
                foreach (var file in job.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sources.Add(PdfFiles.Open(file));
                }

                job.Progress.Report(0.5);
                PdfFiles.WritePages(
                    job.OutputPath("Merged.pdf"),
                    sources.SelectMany(s => Enumerable.Range(1, s.NumberOfPages).Select(n => (s, n))));
            }
            finally
            {
                sources.ForEach(s => s.Dispose());
            }
        },
        cancellationToken);
}
