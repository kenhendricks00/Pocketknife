using System.Text.RegularExpressions;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Media;
using Pocketknife.Core.Tools.Text;
using Pocketknife.Core.Tools.Video;
using Pocketknife.Tests.Support;

namespace Pocketknife.Tests;

public class MediaToolTests(Engines engines)
{
    private async Task<FFmpeg> FFmpegAsync()
    {
        await engines.Manager.EnsureAsync(EngineCatalog.FFmpeg, cancellationToken: TestContext.Current.CancellationToken);
        return FFmpeg.From(engines.Manager);
    }

    [Fact]
    public async Task Compress_video_makes_a_smaller_h264_mp4_of_the_same_length()
    {
        var ffmpeg = await FFmpegAsync();
        var input = Fixtures.Path("sample.mp4");

        var result = await ToolHarness.RunAsync(engines, new CompressVideoTool(), [input], new() { ["quality"] = "small" });

        var output = Assert.Single(result.Outputs);
        Assert.Empty(result.Failures);
        Assert.Equal("sample.mp4", Path.GetFileName(output));
        Assert.True(new FileInfo(output).Length < new FileInfo(input).Length / 2);
        var info = await ffmpeg.ProbeAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(("h264", "aac", 320, 240), (info.VideoCodec, info.AudioCodec, info.Width, info.Height));
        Assert.InRange(info.Duration.TotalSeconds, 1.9, 2.1);
    }

    [Fact]
    public async Task Compress_video_scales_down_but_never_up()
    {
        var ffmpeg = await FFmpegAsync();

        var down = await ToolHarness.RunAsync(engines, new CompressVideoTool(), [Fixtures.Path("sample.mp4")], new() { ["resolution"] = "480", ["codec"] = "h265" });
        var info = await ffmpeg.ProbeAsync(Assert.Single(down.Outputs), TestContext.Current.CancellationToken);

        Assert.Equal(("hevc", 240), (info.VideoCodec, info.Height));
    }

    [Theory]
    [InlineData("webm", "vp9", "opus")]
    [InlineData("mov", "h264", "aac")]
    [InlineData("mp3", null, "mp3")]
    [InlineData("flac", null, "flac")]
    public async Task Convert_produces_the_chosen_format(string format, string? videoCodec, string audioCodec)
    {
        var ffmpeg = await FFmpegAsync();

        var result = await ToolHarness.RunAsync(engines, new ConvertMediaTool(), [Fixtures.Path("sample.mp4")], new() { ["format"] = format });

        var output = Assert.Single(result.Outputs);
        Assert.Equal("." + format, Path.GetExtension(output));
        var info = await ffmpeg.ProbeAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal((videoCodec, audioCodec), (info.VideoCodec, info.AudioCodec));
    }

    [Fact]
    public async Task Convert_explains_that_audio_cannot_become_video()
    {
        await FFmpegAsync();

        var result = await ToolHarness.RunAsync(engines, new ConvertMediaTool(), [Fixtures.Path("speech.mp3")], new() { ["format"] = "mp4" });

        Assert.Empty(result.Outputs);
        Assert.Contains("no video", Assert.Single(result.Failures).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Video_to_gif_makes_a_looping_gif_of_the_requested_clip()
    {
        var ffmpeg = await FFmpegAsync();

        var result = await ToolHarness.RunAsync(engines, new VideoToGifTool(), [Fixtures.Path("sample.mp4")], new()
        {
            ["width"] = "320",
            ["fps"] = "10",
            ["start"] = 0.5,
            ["duration"] = 1,
        });

        var gif = Assert.Single(result.Outputs);
        var bytes = File.ReadAllBytes(gif);
        Assert.Equal("GIF89a"u8.ToArray(), bytes[..6]);
        Assert.Equal(320, BitConverter.ToUInt16(bytes, 6));
        Assert.True(bytes.AsSpan().IndexOf("NETSCAPE2.0"u8) >= 0, "A looping GIF has a NETSCAPE2.0 loop block.");
        var info = await ffmpeg.ProbeAsync(gif, TestContext.Current.CancellationToken);
        Assert.InRange(info.Duration.TotalSeconds, 0.8, 1.2);
    }

    [Theory]
    [InlineData("mp4", "h264")]
    [InlineData("mp3", null)]
    public async Task Download_video_saves_a_web_video_through_yt_dlp(string format, string? videoCodec)
    {
        var ffmpeg = await FFmpegAsync();
        await engines.Manager.EnsureAsync(EngineCatalog.YtDlp, cancellationToken: TestContext.Current.CancellationToken);
        await using var server = await LocalServer.StartAsync(new Dictionary<string, (byte[], string)>
        {
            ["/media/clip.mp4"] = (File.ReadAllBytes(Fixtures.Path("sample.mp4")), "video/mp4"),
        });

        var result = await ToolHarness.RunAsync(engines, new DownloadVideoTool(), [], new() { ["format"] = format }, text: new Uri(server.BaseUri, "/media/clip.mp4").AbsoluteUri);

        Assert.Empty(result.Failures);
        var output = Assert.Single(result.Outputs);
        Assert.Equal("." + format, Path.GetExtension(output));
        Assert.StartsWith("clip", Path.GetFileName(output), StringComparison.Ordinal);
        var info = await ffmpeg.ProbeAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(videoCodec, info.VideoCodec);
        Assert.InRange(info.Duration.TotalSeconds, 1.8, 2.2);
    }

    [Fact]
    public async Task Download_video_rejects_text_that_is_not_a_link()
    {
        var result = await ToolHarness.RunAsync(engines, new DownloadVideoTool(), [], text: "not a link\nftp://example.com/a.mp4");

        Assert.Empty(result.Outputs);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public async Task Transcribe_writes_text_and_subtitles_for_spoken_audio()
    {
        await FFmpegAsync();

        var result = await ToolHarness.RunAsync(engines, new TranscribeTool(), [Fixtures.Path("speech.mp3")], new() { ["model"] = "tiny", ["language"] = "en" });

        Assert.Empty(result.Failures);
        Assert.Equal(["speech.srt", "speech.txt"], result.Outputs.Select(Path.GetFileName).Order());
        var text = Normalize(File.ReadAllText(result.Outputs.Single(o => o.EndsWith(".txt", StringComparison.Ordinal))));
        Assert.Contains("quick brown fox", text, StringComparison.Ordinal);
        Assert.Contains("lazy dog", text, StringComparison.Ordinal);
        var srt = File.ReadAllText(result.Outputs.Single(o => o.EndsWith(".srt", StringComparison.Ordinal)));
        Assert.Matches(@"^1\r\n\d\d:\d\d:\d\d,\d{3} --> \d\d:\d\d:\d\d,\d{3}\r\n", srt);
    }

    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^a-z ]+", " ").Trim();
}
