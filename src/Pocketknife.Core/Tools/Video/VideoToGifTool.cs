using System.Globalization;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Media;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Tools.Video;

public sealed class VideoToGifTool : Tool
{
    public override string Id => "video-to-gif";

    public override string Name => "Video to GIF";

    public override string Summary => "Turn a video clip into a looping GIF with a clean, optimized palette.";

    public override ToolCategory Category => ToolCategory.Video;

    public override string Glyph => "\uF4A9";

    public override IReadOnlyList<string> Keywords => ["animation", "animated", "meme", "loop"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Video, "videos");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("width", "Width",
        [
            new("320", "320 px"),
            new("480", "480 px"),
            new("640", "640 px"),
            new("800", "800 px"),
            new("original", "Original"),
        ], "480"),
        new ChoiceOption("fps", "Frame rate",
        [
            new("10", "10 fps"),
            new("15", "15 fps"),
            new("20", "20 fps"),
            new("25", "25 fps"),
        ], "15"),
        new NumberOption("start", "Start at", 0, 0, 86_400) { Unit = "seconds", WholeNumber = false },
        new NumberOption("duration", "Length", 0, 0, 600) { Unit = "seconds (0 = to the end)", WholeNumber = false },
        new ToggleOption("loop", "Loop forever", true),
    ];

    public override IReadOnlyList<EngineSpec> RequiredEngines(OptionValues options) => [EngineCatalog.FFmpeg];

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        var ffmpeg = FFmpeg.From(job.Engines);
        var info = await ffmpeg.ProbeAsync(job.File, cancellationToken);
        if (!info.HasVideo)
        {
            throw new ToolException("This file has no video track.");
        }

        var start = job.Options.Number("start");
        if (info.Duration > TimeSpan.Zero && start >= info.Duration.TotalSeconds)
        {
            throw new ToolException($"Start time is past the end of the video ({info.Duration.TotalSeconds:0.#} s).");
        }

        var length = job.Options.Number("duration");
        var remaining = Math.Max(0, info.Duration.TotalSeconds - start);
        var span = TimeSpan.FromSeconds(length > 0 ? Math.Min(length, remaining) : remaining);

        var fps = job.Options.Choice("fps");
        var width = job.Options.Choice("width");
        var scale = width == "original" ? "" : $",scale='min({width},iw)':-1:flags=lanczos";
        var filter = $"fps={fps}{scale},split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";

        List<string> args = [];
        if (start > 0)
        {
            args.AddRange(["-ss", start.ToString(CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-i", job.File]);
        if (length > 0)
        {
            args.AddRange(["-t", length.ToString(CultureInfo.InvariantCulture)]);
        }

        var output = job.OutputPathFor(job.File, ".gif");
        args.AddRange(["-filter_complex", filter, "-an", "-loop", job.Options.Toggle("loop") ? "0" : "-1", output]);
        await ffmpeg.RunAsync(args, span, job.Progress, "Making the GIF", cancellationToken);
    }
}
