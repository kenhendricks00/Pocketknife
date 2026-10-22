using System.Text;
using System.Xml.Linq;
using Toolbox.Core.Imaging;
using Toolbox.Core.Pdf;
using Toolbox.Core.Tools.Documents;
using Toolbox.Core.Tools.Files;
using Toolbox.Core.Tools.Images;
using Toolbox.Tests.Support;
using UglyToad.PdfPig;

namespace Toolbox.Tests;

public class PdfToolTests(Engines engines)
{
    private static List<string> PageTexts(string path)
    {
        using var pdf = PdfDocument.Open(path);
        return [.. pdf.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text)))];
    }

    [Fact]
    public async Task Merge_keeps_every_page_in_the_order_given()
    {
        using var input = new TempDir();
        var first = input.Copy(Fixtures.Path("sample.pdf"));
        var second = Path.Combine(input.Path, "second.pdf");
        File.Copy(first, second);

        var result = await ToolHarness.RunAsync(engines, new MergePdfsTool(), [second, first]);

        var merged = Assert.Single(result.Outputs);
        var pages = PageTexts(merged);
        Assert.Equal(6, pages.Count);
        Assert.StartsWith("Invoice for Jane Doe", pages[0], StringComparison.Ordinal);
        Assert.StartsWith("Page three", pages[2], StringComparison.Ordinal);
        Assert.StartsWith("Invoice for Jane Doe", pages[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merge_needs_two_files()
    {
        var error = await Assert.ThrowsAsync<Toolbox.Core.Running.ToolException>(() =>
            ToolHarness.RunAsync(engines, new MergePdfsTool(), [Fixtures.Path("sample.pdf")]));

        Assert.Contains("at least 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Split_by_page_makes_one_file_per_page()
    {
        var result = await ToolHarness.RunAsync(engines, new SplitPdfTool(), [Fixtures.Path("sample.pdf")]);

        Assert.Equal(["sample (p1).pdf", "sample (p2).pdf", "sample (p3).pdf"], result.Outputs.Select(Path.GetFileName));
        Assert.StartsWith("Page two", Assert.Single(PageTexts(result.Outputs[1])), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Split_by_ranges_follows_the_ranges()
    {
        var result = await ToolHarness.RunAsync(engines, new SplitPdfTool(), [Fixtures.Path("sample.pdf")], new() { ["mode"] = "ranges", ["ranges"] = "2-, 1" });

        Assert.Equal(["sample (p1).pdf", "sample (p2-3).pdf"], result.Outputs.Select(Path.GetFileName));
        Assert.Equal(2, PageTexts(result.Outputs[1]).Count);
    }

    [Fact]
    public async Task Split_reports_a_range_past_the_end()
    {
        var result = await ToolHarness.RunAsync(engines, new SplitPdfTool(), [Fixtures.Path("sample.pdf")], new() { ["mode"] = "ranges", ["ranges"] = "2-9" });

        Assert.Empty(result.Outputs);
        Assert.Contains("3 pages", Assert.Single(result.Failures).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redact_removes_the_text_layer_and_metadata_and_blacks_out_matches()
    {
        var input = Fixtures.Path("sample.pdf");

        var result = await ToolHarness.RunAsync(engines, new RedactPdfTool(), [input], new()
        {
            ["terms"] = "Jane Doe\r\n4111-2222-3333",
            ["emails"] = true,
            ["phones"] = true,
            ["dpi"] = "150",
        });

        Assert.Empty(result.Failures);
        var output = Assert.Single(result.Outputs);
        Assert.Equal("sample (redacted).pdf", Path.GetFileName(output));
        Assert.Contains("redacted 5 matches on 2 pages", Assert.Single(result.Messages), StringComparison.Ordinal);

        Assert.All(PageTexts(output), text => Assert.Equal("", text));
        var raw = Encoding.Latin1.GetString(File.ReadAllBytes(output));
        foreach (var secret in new[] { "Jane", "Confidential", "Quarterly", "example.com", "Helvetica" })
        {
            Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
        }

        using var source = PdfDocument.Open(input);
        var pattern = RedactionFinder.BuildPattern(["Jane Doe"], emails: false, phones: false, matchCase: false)!;
        var nameArea = RedactionFinder.Find(source.GetPage(1), pattern)[0];
        var keptArea = RedactionFinder.Find(source.GetPage(1), RedactionFinder.BuildPattern(["Invoice"], false, false, false)!)[0];

        using var rendered = await PdfRasterizer.OpenAsync(output);
        Assert.Equal(3, rendered.PageCount);
        var page = await rendered.RenderAsync(0, 96);
        var scale = page.Image.Width / page.WidthPoints;
        Assert.Equal(1.0, DarkFraction(page.Image, nameArea, scale), 2);
        Assert.InRange(DarkFraction(page.Image, keptArea, scale), 0.02, 0.6);
    }

    [Fact]
    public async Task Redact_needs_something_to_look_for()
    {
        var result = await ToolHarness.RunAsync(engines, new RedactPdfTool(), [Fixtures.Path("sample.pdf")]);

        Assert.Empty(result.Outputs);
        Assert.Contains("Enter words to redact", Assert.Single(result.Failures).Message, StringComparison.Ordinal);
    }

    private static double DarkFraction(BgraImage image, PageRect area, double scale)
    {
        int dark = 0, total = 0;
        for (var y = (int)((area.Y + 2) * scale); y < (int)((area.Y + area.Height - 2) * scale); y++)
        {
            for (var x = (int)((area.X + 2) * scale); x < (int)((area.X + area.Width - 2) * scale); x++)
            {
                var (b, g, r, _) = image.GetPixel(x, y);
                dark += r + g + b < 120 ? 1 : 0;
                total++;
            }
        }

        return (double)dark / total;
    }
}

public class ImageToolTests(Engines engines)
{
    [Theory]
    [InlineData("fast")]
    [InlineData("quality")]
    public async Task Remove_background_keeps_the_subject_and_clears_the_backdrop(string model)
    {
        var result = await ToolHarness.RunAsync(engines, new RemoveBackgroundTool(), [Fixtures.Path("subject.png")], new() { ["model"] = model });

        Assert.Empty(result.Failures);
        var output = Assert.Single(result.Outputs);
        Assert.Equal("subject.png", Path.GetFileName(output));
        var image = BgraImage.Load(output);
        Assert.Equal((400, 300), (image.Width, image.Height));
        Assert.True(image.GetPixel(200, 140).A > 220, "The ball's center should stay opaque.");
        foreach (var (x, y) in new[] { (5, 5), (394, 5), (5, 294), (394, 294), (60, 150), (340, 150) })
        {
            Assert.True(image.GetPixel(x, y).A < 30, $"Backdrop pixel ({x},{y}) should be transparent but alpha is {image.GetPixel(x, y).A}.");
        }
    }

    [Fact]
    public async Task Remove_background_reports_files_that_are_not_images()
    {
        using var input = new TempDir();
        var fake = Path.Combine(input.Path, "fake.png");
        File.WriteAllText(fake, "not really a png");

        var result = await ToolHarness.RunAsync(engines, new RemoveBackgroundTool(), [fake], new() { ["model"] = "fast" });

        Assert.Contains("could not be read", Assert.Single(result.Failures).Message, StringComparison.Ordinal);
    }
}

public class QrCodeToolTests(Engines engines)
{
    private const string Link = "https://example.com/path?q=windows toolbox&lang=日本";

    [Fact]
    public async Task A_made_png_code_reads_back_to_the_same_text()
    {
        var made = await ToolHarness.RunAsync(engines, new MakeQrCodeTool(), [], new() { ["size"] = 600, ["level"] = "H" }, text: Link);

        var png = Assert.Single(made.Outputs);
        Assert.Equal("QR code.png", Path.GetFileName(png));
        var image = BgraImage.Load(png);
        Assert.InRange(image.Width, 400, 600);

        var read = await ToolHarness.RunAsync(engines, new ReadQrCodeTool(), [png]);

        Assert.Equal([Link], read.Messages);
        Assert.Contains(Link, File.ReadAllText(Assert.Single(read.Outputs)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Svg_output_is_valid_markup()
    {
        var made = await ToolHarness.RunAsync(engines, new MakeQrCodeTool(), [], new() { ["format"] = "svg" }, text: Link);

        var svg = XDocument.Load(Assert.Single(made.Outputs));
        Assert.Equal("svg", svg.Root!.Name.LocalName);
        Assert.Single(svg.Root.Elements(), e => e.Name.LocalName == "path");
    }

    [Fact]
    public async Task One_code_per_line_makes_a_file_per_line()
    {
        var made = await ToolHarness.RunAsync(engines, new MakeQrCodeTool(), [], new() { ["perLine"] = true }, text: "alpha\n\nbeta\r\ngamma");

        Assert.Equal(["QR 001 - alpha.png", "QR 002 - beta.png", "QR 003 - gamma.png"], made.Outputs.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Reads_a_code_inside_a_larger_noisy_picture()
    {
        using var input = new TempDir();
        var code = QrCodes.ToImage(QrCodes.Encode("hello from a photo", "M"), 200);
        var random = new Random(7);
        var picture = new BgraImage(640, 480, new byte[640 * 480 * 4]);
        random.NextBytes(picture.Pixels);
        for (var i = 3; i < picture.Pixels.Length; i += 4)
        {
            picture.Pixels[i] = 255;
        }

        for (var y = 0; y < code.Height; y++)
        {
            Array.Copy(code.Pixels, y * code.Stride, picture.Pixels, ((y + 150) * picture.Width + 300) * 4, code.Stride);
        }

        var file = Path.Combine(input.Path, "photo.png");
        picture.SavePng(file);

        var read = await ToolHarness.RunAsync(engines, new ReadQrCodeTool(), [file]);

        Assert.Equal(["hello from a photo"], read.Messages);
    }

    [Fact]
    public async Task Reports_when_there_is_no_code()
    {
        var read = await ToolHarness.RunAsync(engines, new ReadQrCodeTool(), [Fixtures.Path("subject.png")]);

        Assert.Empty(read.Outputs);
        Assert.Contains("No QR code", Assert.Single(read.Failures).Message, StringComparison.Ordinal);
    }
}
