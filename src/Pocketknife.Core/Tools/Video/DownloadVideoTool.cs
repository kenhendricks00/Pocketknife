using System.Globalization;
using System.Text.RegularExpressions;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Tools.Video;

public sealed partial class DownloadVideoTool : Tool
{
    public override string Id => "download-video";

    public override string Name => "Download Video";

    public override string Summary => "Save videos or their audio from YouTube and over a thousand other sites.";

    public override ToolCategory Category => ToolCategory.Video;

    public override string Glyph => "\uE896";

    public override IReadOnlyList<string> Keywords => ["youtube", "save", "link", "url", "yt-dlp", "music", "mp3"];

    public override ToolInput Input { get; } = new TextInput("Video links", "Paste one link per line", SplitLines: true);

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("format", "Save as",
        [
            new("mp4", "Video (MP4)"),
            new("mp3", "Audio only (MP3)"),
            new("m4a", "Audio only (M4A)"),
        ], "mp4"),
        new ChoiceOption("quality", "Maximum quality",
        [
            new("best", "Best available"),
            new("2160", "4K (2160p)"),
            new("1080", "1080p"),
            new("720", "720p"),
            new("480", "480p"),
        ], "1080") { VisibleWhen = new("format", ["mp4"]) },
        new ToggleOption("playlist", "Download the whole playlist when a link is part of one", false),
    ];

    public override IReadOnlyList<EngineSpec> RequiredEngines(OptionValues options) => [EngineCatalog.YtDlp, EngineCatalog.FFmpeg];

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(job.Text, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            throw new ToolException("This is not a web link. Links start with https://");
        }

        var ytDlp = job.Engines.Require(EngineCatalog.YtDlp);
        var ffmpegDirectory = Path.GetDirectoryName(job.Engines.Require(EngineCatalog.FFmpeg))!;
        var temp = Path.Combine(Path.GetTempPath(), "Pocketknife", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        List<string> args =
        [
            "--ignore-config", "--color", "never", "--newline", "--no-mtime", "--windows-filenames",
            job.Options.Toggle("playlist") ? "--yes-playlist" : "--no-playlist",
            "--ffmpeg-location", ffmpegDirectory,
            "-P", job.OutputDirectory, "-P", $"temp:{temp}",
            "-o", "%(title).120B [%(id)s].%(ext)s",
            "--progress-template", "download:[progress] %(progress._percent_str)s",
            "-N", "4",
        ];

        var format = job.Options.Choice("format");
        if (format == "mp4")
        {
            var quality = job.Options.Choice("quality");
            args.AddRange(["-f", "bv*+ba/b", "-S", quality == "best" ? "ext:mp4:m4a" : $"res:{quality},vcodec:h264,ext:mp4:m4a", "--merge-output-format", "mp4"]);
        }
        else
        {
            args.AddRange(["-f", "ba/b", "-x", "--audio-format", format, "--audio-quality", "0"]);
        }

        args.Add(url.AbsoluteUri);

        try
        {
            var result = await ProcessRunner.RunAsync(
                ytDlp,
                args,
                line =>
                {
                    var match = PercentPattern().Match(line);
                    if (line.StartsWith("[progress]", StringComparison.Ordinal) && match.Success)
                    {
                        job.Progress.Report(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 100);
                    }
                    else
                    {
                        job.Log(line);
                    }
                },
                job.Log,
                cancellationToken);
            result.EnsureSuccess("Download");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)%")]
    private static partial Regex PercentPattern();
}
