using System.Text;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Media;
using Pocketknife.Core.Running;
using Whisper.net;

namespace Pocketknife.Core.Tools.Text;

public sealed class TranscribeTool : Tool
{
    public override string Id => "transcribe";

    public override string Name => "Transcribe";

    public override string Summary => "Turn speech in audio or video into text or subtitles, on this PC with Whisper.";

    public override ToolCategory Category => ToolCategory.TextAndSpeech;

    public override string Glyph => "\uE720";

    public override IReadOnlyList<string> Keywords => ["speech to text", "subtitles", "captions", "srt", "whisper", "dictation"];

    public override ToolInput Input { get; } = new FileInput(FileTypes.Media, "audio or video files");

    public override IReadOnlyList<ToolOption> Options { get; } =
    [
        new ChoiceOption("model", "Accuracy",
        [
            new("base", "Better (148 MB model)"),
            new("tiny", "Faster (78 MB model)"),
        ], "base"),
        new ChoiceOption("language", "Spoken language",
        [
            new("auto", "Detect automatically"),
            new("en", "English"),
            new("es", "Spanish"),
            new("fr", "French"),
            new("de", "German"),
            new("it", "Italian"),
            new("pt", "Portuguese"),
            new("nl", "Dutch"),
            new("pl", "Polish"),
            new("ru", "Russian"),
            new("uk", "Ukrainian"),
            new("tr", "Turkish"),
            new("ar", "Arabic"),
            new("hi", "Hindi"),
            new("ja", "Japanese"),
            new("ko", "Korean"),
            new("zh", "Chinese"),
        ], "auto"),
        new ChoiceOption("output", "Save as",
        [
            new("both", "Text (.txt) and subtitles (.srt)"),
            new("txt", "Text (.txt)"),
            new("srt", "Subtitles (.srt)"),
        ], "both"),
    ];

    public override IReadOnlyList<EngineSpec> RequiredEngines(OptionValues options) =>
        [EngineCatalog.FFmpeg, Model(options)];

    private static EngineSpec Model(OptionValues options) =>
        options.Choice("model") == "tiny" ? EngineCatalog.WhisperTiny : EngineCatalog.WhisperBase;

    public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
    {
        var ffmpeg = FFmpeg.From(job.Engines);
        var info = await ffmpeg.ProbeAsync(job.File, cancellationToken);
        if (!info.HasAudio)
        {
            throw new ToolException("This file has no audio to transcribe.");
        }

        var wav = Path.Combine(Path.GetTempPath(), $"Pocketknife-{Guid.NewGuid():N}.wav");
        try
        {
            var decode = new Progress<double>(f => job.Progress.Report(f * 0.1));
            await ffmpeg.RunAsync(
                ["-i", job.File, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav],
                info.Duration,
                decode,
                "Reading the audio",
                cancellationToken);

            var segments = await TranscribeAsync(job, wav, info.Duration, cancellationToken);
            if (segments.Count == 0)
            {
                throw new ToolException("No speech was found.");
            }

            var output = job.Options.Choice("output");
            if (output is "both" or "txt")
            {
                await File.WriteAllTextAsync(job.OutputPathFor(job.File, ".txt"), Transcript.ToText(segments), Encoding.UTF8, cancellationToken);
            }

            if (output is "both" or "srt")
            {
                await File.WriteAllTextAsync(job.OutputPathFor(job.File, ".srt"), Transcript.ToSrt(segments), Encoding.UTF8, cancellationToken);
            }
        }
        finally
        {
            File.Delete(wav);
        }
    }

    private static async Task<List<TranscriptSegment>> TranscribeAsync(ToolJob job, string wav, TimeSpan duration, CancellationToken cancellationToken)
    {
        var modelPath = job.Engines.Require(Model(job.Options));
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguage(job.Options.Choice("language"))
            .WithThreads(Math.Max(1, Environment.ProcessorCount - 1))
            .Build();

        var segments = new List<TranscriptSegment>();
        await using var audio = File.OpenRead(wav);
        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken))
        {
            segments.Add(new TranscriptSegment(segment.Start, segment.End, segment.Text));
            if (duration > TimeSpan.Zero)
            {
                job.Progress.Report(0.1 + (0.9 * Math.Min(1, segment.End / duration)));
            }
        }

        return segments;
    }
}
