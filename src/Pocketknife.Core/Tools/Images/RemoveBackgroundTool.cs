using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Imaging;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Tools.Images;

public sealed class RemoveBackgroundTool : Tool
{
    private sealed record ModelProfile(EngineSpec Engine, int Size, float[] Mean, float[] Std);

    private static readonly ModelProfile s_quality = new(EngineCatalog.IsNet, 1024, [0.5f, 0.5f, 0.5f], [1f, 1f, 1f]);
    private static readonly ModelProfile s_fast = new(EngineCatalog.U2NetP, 320, [0.485f, 0.456f, 0.406f], [0.229f, 0.224f, 0.225f]);
    private static readonly ConcurrentDictionary<string, Lazy<InferenceSession>> s_sessions = new();

    public override string Id => "remove-background";

    public override string Name => "Remove Background";

    public override string Summary => "Cut the subject out of a photo and save it as a transparent PNG.";

    public override ToolCategory Category => ToolCategory.Images;

    public override string Glyph => "\uE91B";

    public override IReadOnlyList<string> Keywords => ["cutout", "transparent", "png", "erase", "isolate", "subject"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Image, "images");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("model", "Model",
        [
            new("quality", "Best quality (179 MB model)"),
            new("fast", "Fast (5 MB model)"),
        ], "quality"),
    ];

    public override IReadOnlyList<EngineSpec> RequiredEngines(OptionValues options) => [Profile(options).Engine];

    private static ModelProfile Profile(OptionValues options) => options.Choice("model") == "fast" ? s_fast : s_quality;

    public override Task RunAsync(ToolJob job, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            var profile = Profile(job.Options);
            var image = BgraImage.Load(job.File);
            job.Progress.Report(0.1);
            cancellationToken.ThrowIfCancellationRequested();

            var session = s_sessions.GetOrAdd(
                job.Engines.Require(profile.Engine),
                path => new Lazy<InferenceSession>(() => new InferenceSession(path))).Value;
            var mask = PredictMask(session, image, profile);
            job.Progress.Report(0.8);
            cancellationToken.ThrowIfCancellationRequested();

            ApplyMask(image, mask, profile.Size);
            image.SavePng(job.OutputPathFor(job.File, ".png"));
        },
        cancellationToken);

    /// <summary>Returns the model's foreground probability (0..1) on a size x size grid.</summary>
    private static float[] PredictMask(InferenceSession session, BgraImage image, ModelProfile profile)
    {
        var size = profile.Size;
        var resized = new float[3 * size * size];
        var max = 1e-6f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var (b, g, r) = Sample(image, (x + 0.5f) * image.Width / size - 0.5f, (y + 0.5f) * image.Height / size - 0.5f);
                var i = y * size + x;
                resized[i] = r;
                resized[size * size + i] = g;
                resized[2 * size * size + i] = b;
                max = Math.Max(max, Math.Max(r, Math.Max(g, b)));
            }
        }

        var tensor = new DenseTensor<float>([1, 3, size, size]);
        var buffer = tensor.Buffer.Span;
        for (var c = 0; c < 3; c++)
        {
            for (var i = 0; i < size * size; i++)
            {
                buffer[c * size * size + i] = (resized[c * size * size + i] / max - profile.Mean[c]) / profile.Std[c];
            }
        }

        var inputName = session.InputMetadata.Keys.First();
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var output = results[0].AsEnumerable<float>().Take(size * size).ToArray();
        var min = output.Min();
        var range = Math.Max(output.Max() - min, 1e-6f);
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = (output[i] - min) / range;
        }

        return output;
    }

    private static void ApplyMask(BgraImage image, float[] mask, int size)
    {
        for (var y = 0; y < image.Height; y++)
        {
            var my = Math.Clamp((y + 0.5f) * size / image.Height - 0.5f, 0, size - 1);
            for (var x = 0; x < image.Width; x++)
            {
                var mx = Math.Clamp((x + 0.5f) * size / image.Width - 0.5f, 0, size - 1);
                var alpha = Bilinear(mask, size, mx, my);
                var i = ((y * image.Width) + x) * 4 + 3;
                image.Pixels[i] = (byte)Math.Clamp(image.Pixels[i] * alpha + 0.5f, 0, 255);
            }
        }
    }

    private static (float B, float G, float R) Sample(BgraImage image, float x, float y)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
        float fx = x - x0, fy = y - y0;
        var p = image.Pixels;
        float Channel(int c) =>
            ((p[(y0 * image.Width + x0) * 4 + c] * (1 - fx)) + (p[(y0 * image.Width + x1) * 4 + c] * fx)) * (1 - fy)
            + ((p[(y1 * image.Width + x0) * 4 + c] * (1 - fx)) + (p[(y1 * image.Width + x1) * 4 + c] * fx)) * fy;
        return (Channel(0), Channel(1), Channel(2));
    }

    private static float Bilinear(float[] grid, int size, float x, float y)
    {
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, size - 1), y1 = Math.Min(y0 + 1, size - 1);
        float fx = x - x0, fy = y - y0;
        return ((grid[y0 * size + x0] * (1 - fx)) + (grid[y0 * size + x1] * fx)) * (1 - fy)
            + ((grid[y1 * size + x0] * (1 - fx)) + (grid[y1 * size + x1] * fx)) * fy;
    }
}
