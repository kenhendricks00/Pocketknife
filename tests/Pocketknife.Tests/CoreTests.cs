using Pocketknife.Core.Media;
using Pocketknife.Core.Pdf;
using Pocketknife.Core.Running;
using Pocketknife.Core.Settings;
using Pocketknife.Core.Tools;
using Pocketknife.Tests.Support;

namespace Pocketknife.Tests;

public class OptionValuesTests
{
    private static readonly IReadOnlyList<ToolOption> s_schema =
    [
        new ChoiceOption("mode", "Mode", [new("a", "A"), new("b", "B")], "a"),
        new NumberOption("count", "Count", 2, 1, 10),
        new ToggleOption("flag", "Flag", false),
    ];

    [Fact]
    public void Uses_defaults_when_nothing_is_set()
    {
        var values = new OptionValues(s_schema);

        Assert.Equal("a", values.Choice("mode"));
        Assert.Equal(2, values.WholeNumber("count"));
        Assert.False(values.Toggle("flag"));
    }

    [Fact]
    public void Coerces_text_from_the_ui()
    {
        var values = new OptionValues(s_schema, new Dictionary<string, object?> { ["count"] = "7", ["flag"] = "true", ["mode"] = "b" });

        Assert.Equal(7, values.WholeNumber("count"));
        Assert.True(values.Toggle("flag"));
        Assert.Equal("b", values.Choice("mode"));
    }

    [Theory]
    [InlineData("mode", "z")]
    [InlineData("count", "11")]
    [InlineData("count", "lots")]
    [InlineData("unknown", "1")]
    public void Rejects_invalid_values(string key, string value) =>
        Assert.Throws<ArgumentException>(() => new OptionValues(s_schema, new Dictionary<string, object?> { [key] = value }));
}

public class PageRangesTests
{
    [Fact]
    public void Parses_single_pages_ranges_and_open_ends() =>
        Assert.Equal([new PageRange(1, 3), new PageRange(5, 5), new PageRange(8, 10)], PageRanges.Parse("1-3, 5, 8-", 10));

    [Theory]
    [InlineData("4-2")]
    [InlineData("1-11")]
    [InlineData("0")]
    [InlineData("a-b")]
    [InlineData("1-2-3")]
    [InlineData("  ")]
    public void Rejects_bad_ranges(string text) => Assert.Throws<ToolException>(() => PageRanges.Parse(text, 10));

    [Fact]
    public void Chunks_cover_every_page_once() =>
        Assert.Equal([new PageRange(1, 3), new PageRange(4, 6), new PageRange(7, 7)], PageRanges.Chunks(7, 3));
}

public class OutputNamesTests
{
    [Fact]
    public void Never_reuses_a_name()
    {
        using var dir = new TempDir();

        var first = OutputNames.Unique(dir.Path, "clip.mp4");
        var second = OutputNames.Unique(dir.Path, "clip.mp4");

        Assert.Equal("clip.mp4", Path.GetFileName(first));
        Assert.Equal("clip (2).mp4", Path.GetFileName(second));
    }

    [Fact]
    public void Removes_characters_windows_does_not_allow() =>
        Assert.Equal("a_b_c_", OutputNames.Sanitize("a:b?c*"));

    [Fact]
    public void Run_folders_are_dated_and_unique()
    {
        using var dir = new TempDir();
        var now = new DateTimeOffset(2026, 10, 21, 16, 30, 5, TimeSpan.Zero);

        var first = OutputNames.CreateRunDirectory(dir.Path, "Compress Video", now);
        var second = OutputNames.CreateRunDirectory(dir.Path, "Compress Video", now);

        Assert.Equal("2026-10-21 16.30.05 Compress Video", Path.GetFileName(first));
        Assert.Equal("2026-10-21 16.30.05 Compress Video (2)", Path.GetFileName(second));
    }
}

public class ToolSearchTests
{
    [Theory]
    [InlineData("gif", "video-to-gif")]
    [InlineData("comp", "compress-video")]
    [InlineData("cv", "compress-video")]
    [InlineData("youtube", "download-video")]
    [InlineData("subtitles", "transcribe")]
    [InlineData("black out", "redact-pdf")]
    [InlineData("merge", "merge-pdfs")]
    [InlineData("qr read", "read-qr-code")]
    public void Best_match_comes_first(string query, string expected) =>
        Assert.Equal(expected, ToolSearch.Rank(ToolRegistry.All, query)[0].Id);

    [Fact]
    public void Unmatched_queries_return_nothing() => Assert.Empty(ToolSearch.Rank(ToolRegistry.All, "zzzz"));
}

public class ToolRegistryTests
{
    [Fact]
    public void Tool_ids_are_unique() =>
        Assert.Equal(ToolRegistry.All.Count, ToolRegistry.All.Select(t => t.Id).Distinct().Count());

    [Fact]
    public void Every_option_schema_is_consistent()
    {
        foreach (var tool in ToolRegistry.All)
        {
            var values = new OptionValues(tool.Options);
            Assert.NotNull(tool.RequiredEngines(values));
            foreach (var option in tool.Options)
            {
                if (option.VisibleWhen is { } condition)
                {
                    var target = Assert.IsType<ChoiceOption>(tool.Options.Single(o => o.Key == condition.Key));
                    Assert.All(condition.Values, v => Assert.Contains(target.Choices, c => c.Value == v));
                }
            }
        }
    }
}

public class TranscriptTests
{
    [Fact]
    public void Writes_numbered_srt_cues()
    {
        var srt = Transcript.ToSrt(
        [
            new(TimeSpan.Zero, TimeSpan.FromMilliseconds(1500), " Hello there. "),
            new(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(3723.004), "Second line"),
        ]);

        Assert.Equal("1\r\n00:00:00,000 --> 00:00:01,500\r\nHello there.\r\n\r\n2\r\n00:00:01,500 --> 01:02:03,004\r\nSecond line\r\n\r\n", srt);
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Round_trips_and_reads_hand_written_files()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Theme = AppTheme.Dark, OutputRoot = @"D:\Out", EnginePaths = { ["ffmpeg"] = @"C:\ffmpeg.exe" } });

        Assert.Contains("\"Dark\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal((AppTheme.Dark, @"D:\Out", @"C:\ffmpeg.exe"), (store.Load().Theme, store.Load().OutputRoot, store.Load().EnginePaths["ffmpeg"]));

        File.WriteAllText(path, """{ "Theme": "Light", "LauncherHotkey": "Ctrl+Shift+Space" }""");
        Assert.Equal((AppTheme.Light, "Ctrl+Shift+Space", true), (store.Load().Theme, store.Load().LauncherHotkey, store.Load().KeepRunningInTray));
    }

    [Fact]
    public void Falls_back_to_defaults_for_a_damaged_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(new AppSettings().LauncherHotkey, new SettingsStore(path).Load().LauncherHotkey);
    }

    [Fact]
    public void Default_output_folder_is_under_downloads() =>
        Assert.Equal(Path.Combine(AppPaths.DownloadsFolder, "Pocketknife"), new AppSettings().EffectiveOutputRoot);
}
