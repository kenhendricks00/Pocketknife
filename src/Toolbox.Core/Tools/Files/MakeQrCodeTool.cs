using System.Text;
using Toolbox.Core.Imaging;
using Toolbox.Core.Running;

namespace Toolbox.Core.Tools.Files;

public sealed class MakeQrCodeTool : Tool
{
    public override string Id => "make-qr-code";

    public override string Name => "Make QR Code";

    public override string Summary => "Create a QR code for a link, Wi-Fi details or any text, as PNG or SVG.";

    public override ToolCategory Category => ToolCategory.Files;

    public override string Glyph => "\uED14";

    public override IReadOnlyList<string> Keywords => ["qr", "barcode", "generate", "create", "link", "code"];

    public override ToolInput Input { get; } = new TextInput("Text or link", "https://example.com", SplitLines: false);

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("format", "Format", [new("png", "PNG image"), new("svg", "SVG (scales to any size)")], "png"),
        new NumberOption("size", "Image size", 1024, 64, 8192) { Unit = "pixels", VisibleWhen = new("format", ["png"]) },
        new ChoiceOption("level", "Error correction",
        [
            new("L", "Low (7%)"),
            new("M", "Medium (15%)"),
            new("Q", "High (25%)"),
            new("H", "Highest (30%)"),
        ], "M") { Help = "Higher levels still scan when part of the code is damaged or covered, but make denser codes." },
        new ToggleOption("perLine", "Make one code per line", false),
    ];

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        var texts = job.Options.Toggle("perLine")
            ? job.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : [job.Text.Replace("\r\n", "\n", StringComparison.Ordinal)];

        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matrix = QrCodes.Encode(texts[i], job.Options.Choice("level"));
            var name = texts.Count == 1 ? "QR code" : $"QR {i + 1:000} - {OutputNames.Sanitize(texts[i], 40)}";
            if (job.Options.Choice("format") == "svg")
            {
                await File.WriteAllTextAsync(job.OutputPath(name + ".svg"), QrCodes.ToSvg(matrix), new UTF8Encoding(false), cancellationToken);
            }
            else
            {
                QrCodes.ToImage(matrix, job.Options.WholeNumber("size")).SavePng(job.OutputPath(name + ".png"));
            }

            job.Progress.Report((i + 1.0) / texts.Count);
        }
    }
}
