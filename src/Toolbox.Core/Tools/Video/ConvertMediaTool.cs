using Toolbox.Core.Engines;
using Toolbox.Core.Media;
using Toolbox.Core.Running;

namespace Toolbox.Core.Tools.Video;

public sealed class ConvertMediaTool : Tool
{
    private sealed record Target(string Extension, bool IsVideo, string[] Codecs);

    private static readonly Dictionary<string, Target> s_targets = new()
    {
        ["mp4"] = new(".mp4", true, ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
        ["webm"] = new(".webm", true, ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k"]),
        ["mov"] = new(".mov", true, ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"]),
        ["mkv"] = new(".mkv", true, ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-b:a", "192k"]),
        ["mp3"] = new(".mp3", false, ["-c:a", "libmp3lame", "-q:a", "2"]),
        ["m4a"] = new(".m4a", false, ["-c:a", "aac", "-b:a", "192k"]),
        ["wav"] = new(".wav", false, ["-c:a", "pcm_s16le"]),
        ["flac"] = new(".flac", false, ["-c:a", "flac"]),
        ["ogg"] = new(".ogg", false, ["-c:a", "libopus", "-b:a", "128k"]),
    };

    public override string Id => "convert-media";

    public override string Name => "Convert Video & Audio";

    public override string Summary => "Change a video or audio file to another format, or pull the audio out of a video.";

    public override ToolCategory Category => ToolCategory.Video;

    public override string Glyph => "\uE8AB";

    public override IReadOnlyList<string> Keywords => ["format", "mp4", "webm", "mov", "mkv", "mp3", "wav", "flac", "extract audio"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Media, "video or audio files");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("format", "Convert to",
        [
            new("mp4", "MP4 video (H.264)"),
            new("webm", "WebM video (VP9)"),
            new("mov", "MOV video (H.264)"),
            new("mkv", "MKV video (H.264)"),
            new("mp3", "MP3 audio"),
            new("m4a", "M4A audio (AAC)"),
            new("wav", "WAV audio (uncompressed)"),
            new("flac", "FLAC audio (lossless)"),
            new("ogg", "OGG audio (Opus)"),
        ], "mp4"),
    ];

    public override IReadOnlyList<EngineSpec> RequiredEngines(OptionValues options) => [EngineCatalog.FFmpeg];

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        var ffmpeg = FFmpeg.From(job.Engines);
        var info = await ffmpeg.ProbeAsync(job.File, cancellationToken);
        var target = s_targets[job.Options.Choice("format")];
        if (target.IsVideo && !info.HasVideo)
        {
            throw new ToolException("This file has no video. Choose an audio format instead.");
        }

        if (!info.HasAudio && !target.IsVideo)
        {
            throw new ToolException("This file has no audio track.");
        }

        var output = job.OutputPathFor(job.File, target.Extension);
        List<string> args = target.IsVideo
            ? ["-i", job.File, "-map", "0:v:0", "-map", "0:a:0?"]
            : ["-i", job.File, "-map", "0:a:0", "-vn"];
        args.AddRange(target.Codecs);
        args.Add(output);
        await ffmpeg.RunAsync(args, info.Duration, job.Progress, "Converting", cancellationToken);
    }
}
