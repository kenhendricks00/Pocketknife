using System.Globalization;
using System.Text;
using System.Text.Json;
using Toolbox.Core.Engines;
using Toolbox.Core.Running;

namespace Toolbox.Core.Media;

public sealed record MediaInfo(TimeSpan Duration, bool HasVideo, bool HasAudio, int Width, int Height, string? VideoCodec, string? AudioCodec);

public sealed class FFmpeg(string ffmpegPath, string ffprobePath)
{
    public static FFmpeg From(EngineManager engines) =>
        new(engines.Require(EngineCatalog.FFmpeg), engines.RequireFile(EngineCatalog.FFmpeg, "ffprobe.exe"));

    public string ExecutablePath => ffmpegPath;

    public async Task<MediaInfo> ProbeAsync(string file, CancellationToken cancellationToken)
    {
        var json = new StringBuilder();
        var result = await ProcessRunner.RunAsync(
            ffprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", file],
            line => json.AppendLine(line),
            cancellationToken: cancellationToken);
        result.EnsureSuccess($"Reading {Path.GetFileName(file)}");

        using var doc = JsonDocument.Parse(json.ToString());
        var streams = doc.RootElement.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
        var video = streams.FirstOrDefault(x => Codec(x, "codec_type") == "video" && !IsAttachedPicture(x));
        var audio = streams.FirstOrDefault(x => Codec(x, "codec_type") == "audio");
        var duration = doc.RootElement.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var d)
            && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;

        return new MediaInfo(
            duration,
            video.ValueKind != JsonValueKind.Undefined,
            audio.ValueKind != JsonValueKind.Undefined,
            video.ValueKind != JsonValueKind.Undefined && video.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
            video.ValueKind != JsonValueKind.Undefined && video.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
            video.ValueKind != JsonValueKind.Undefined ? Codec(video, "codec_name") : null,
            audio.ValueKind != JsonValueKind.Undefined ? Codec(audio, "codec_name") : null);
    }

    /// <summary>Runs ffmpeg with the given arguments, reporting progress against <paramref name="duration"/>.</summary>
    public async Task RunAsync(IEnumerable<string> arguments, TimeSpan duration, IProgress<double> progress, string what, CancellationToken cancellationToken)
    {
        string[] prefix = ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-progress", "pipe:1", "-nostats"];
        var result = await ProcessRunner.RunAsync(
            ffmpegPath,
            [.. prefix, .. arguments],
            line =>
            {
                if (duration > TimeSpan.Zero
                    && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
                {
                    progress.Report(Math.Clamp(micros / 1_000_000.0 / duration.TotalSeconds, 0, 1));
                }
            },
            cancellationToken: cancellationToken);
        result.EnsureSuccess(what);
        progress.Report(1);
    }

    private static string? Codec(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition)
        && disposition.TryGetProperty("attached_pic", out var attached)
        && attached.GetInt32() == 1;
}
