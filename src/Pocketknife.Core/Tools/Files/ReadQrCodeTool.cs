using System.Globalization;
using System.Text;
using Pocketknife.Core.Imaging;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Tools.Files;

public sealed class ReadQrCodeTool : Tool
{
    public override string Id => "read-qr-code";

    public override string Name => "Read QR Code";

    public override string Summary => "Read the text or link inside QR codes in screenshots and photos.";

    public override ToolCategory Category => ToolCategory.Files;

    public override string Glyph => "\uE722";

    public override IReadOnlyList<string> Keywords => ["qr", "scan", "decode", "barcode", "screenshot"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Image, "images", Combine: true);

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        var report = new StringBuilder();
        var found = 0;
        for (var i = 0; i < job.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = job.Files[i];
            report.AppendLine(Path.GetFileName(file));
            try
            {
                var codes = await Task.Run(() => QrCodes.Decode(BgraImage.Load(file)), cancellationToken);
                foreach (var code in codes)
                {
                    report.AppendLine(code);
                    job.Report(job.Files.Count == 1 ? code : $"{Path.GetFileName(file)}: {code}");
                }

                found += codes.Count;
                if (codes.Count == 0)
                {
                    report.AppendLine("(no QR code found)");
                }
            }
            catch (ToolException ex)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"(could not read: {ex.Message})");
            }

            report.AppendLine();
            job.Progress.Report((i + 1.0) / job.Files.Count);
        }

        if (found == 0)
        {
            throw new ToolException(job.Files.Count == 1 ? "No QR code was found in this image." : "No QR codes were found in these images.");
        }

        await File.WriteAllTextAsync(job.OutputPath("QR codes.txt"), report.ToString(), Encoding.UTF8, cancellationToken);
    }
}
