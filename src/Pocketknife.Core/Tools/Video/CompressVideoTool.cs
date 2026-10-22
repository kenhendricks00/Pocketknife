using System.Globalization;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Media;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Tools.Video;

public sealed class CompressVideoTool : Tool
{
    public override string Id => "compress-video";

    public override string Name => "Compress Video";

    public override string Summary => "Make videos smaller to share or store, with a quality level you choose.";

    public override ToolCategory Category => ToolCategory.Video;

    public override string Glyph => "\uE73F";

    public override IReadOnlyList<string> Keywords => ["shrink", "reduce", "size", "mp4", "h264", "hevc"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Video, "videos");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("quality", "Quality",
        [
            new("high", "High (larger file)"),
            new("balanced", "Balanced"),
            new("small", "Smallest file"),
        ], "balanced"),
        new ChoiceOption("resolution", "Maximum resolution",
        [
            new("original", "Keep original"),
            new("1080", "1080p"),
            new("720", "720p"),
            new("480", "480p"),
        ], "original"),
        new ChoiceOption("codec", "Format",
        [
            new("h264", "MP4 · H.264 (plays everywhere)"),
            new("h265", "MP4 · H.265 (smaller, newer devices)"),
        ], "h264"),
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

        var h265 = job.Options.Choice("codec") == "h265";
        var crf = (job.Options.Choice("quality"), h265) switch
        {
            ("high", false) => 23,
            ("balanced", false) => 28,
            ("small", false) => 32,
            ("high", true) => 26,
            ("balanced", true) => 30,
            _ => 34,
        };

        var output = job.OutputPathFor(job.File, ".mp4");
        List<string> args = ["-i", job.File, "-map", "0:v:0", "-map", "0:a:0?"];
        if (job.Options.Choice("resolution") is var resolution and not "original")
        {
            args.AddRange(["-vf", $"scale=-2:'min({resolution},ih)'"]);
        }

        args.AddRange(h265
            ? ["-c:v", "libx265", "-tag:v", "hvc1", "-x265-params", "log-level=error"]
            : ["-c:v", "libx264"]);
        args.AddRange(["-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p"]);
        args.AddRange(["-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", output]);

        await ffmpeg.RunAsync(args, info.Duration, job.Progress, "Compressing", cancellationToken);

        var before = new FileInfo(job.File).Length;
        var after = new FileInfo(output).Length;
        job.Log($"{Path.GetFileName(job.File)}: {before:N0} → {after:N0} bytes");
    }
}
