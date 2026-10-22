using System.IO.Compression;
using System.Security.Cryptography;
using Toolbox.Core.Engines;
using Toolbox.Core.Running;
using Toolbox.Core.Tools;
using Toolbox.Tests.Support;

namespace Toolbox.Tests;

public class ToolRunnerTests(Engines engines)
{
    private sealed class CopyTool : Tool
    {
        public override string Id => "copy";

        public override string Name => "Copy";

        public override string Summary => "Copies text files, failing on ones that say 'fail'.";

        public override ToolCategory Category => ToolCategory.Files;

        public override string Glyph => "";

        public override ToolInput Input { get; } = new FileInput([".txt"], "text files");

        public override async Task RunAsync(ToolJob job, CancellationToken cancellationToken)
        {
            var output = job.OutputPathFor(job.File, ".txt");
            var text = await File.ReadAllTextAsync(job.File, cancellationToken);
            if (text == "fail")
            {
                throw new ToolException("Asked to fail.");
            }

            await File.WriteAllTextAsync(output, text.ToUpperInvariant(), cancellationToken);
        }
    }

    [Fact]
    public async Task Batch_runs_every_file_in_a_folder_and_keeps_going_after_a_failure()
    {
        using var input = new TempDir();
        File.WriteAllText(Path.Combine(input.Path, "a.txt"), "one");
        File.WriteAllText(Path.Combine(input.Path, "b.txt"), "fail");
        File.WriteAllText(Path.Combine(input.Path, "c.txt"), "three");
        File.WriteAllText(Path.Combine(input.Path, "ignored.jpg"), "x");
        Directory.CreateDirectory(Path.Combine(input.Path, "sub"));
        File.WriteAllText(Path.Combine(input.Path, "sub", "d.txt"), "nested");

        var result = await ToolHarness.RunAsync(engines, new CopyTool(), [input.Path]);

        Assert.Equal(["a.txt", "c.txt"], result.Outputs.Select(Path.GetFileName));
        Assert.Equal("THREE", File.ReadAllText(result.Outputs[1]));
        var failure = Assert.Single(result.Failures);
        Assert.Equal(("b.txt", "Asked to fail."), (failure.Item, failure.Message));
    }

    [Fact]
    public async Task Unsupported_files_are_reported_and_no_empty_folder_is_left()
    {
        using var input = new TempDir();
        var jpg = Path.Combine(input.Path, "photo.jpg");
        File.WriteAllText(jpg, "x");
        using var outputRoot = new TempDir();
        var runner = new ToolRunner(engines.Manager, () => outputRoot.Path);

        await Assert.ThrowsAsync<ToolException>(() => runner.RunAsync(new CopyTool(), new RunRequest([jpg]), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFileSystemEntries(outputRoot.Path));
    }

    [Fact]
    public async Task A_failed_job_leaves_no_partial_files()
    {
        using var input = new TempDir();
        var file = Path.Combine(input.Path, "b.txt");
        File.WriteAllText(file, "fail");

        var result = await ToolHarness.RunAsync(engines, new CopyTool(), [file]);

        Assert.Empty(result.Outputs);
        Assert.Null(result.OutputDirectory);
        Assert.Single(result.Failures);
    }
}

public class EngineManagerTests
{
    private static EngineSpec Spec(Uri baseUri, string path, byte[] body, string[] files) => new(
        "test-engine",
        "Test engine",
        "1.0",
        new Uri(baseUri, path),
        Convert.ToHexStringLower(SHA256.HashData(body)),
        body.Length,
        "MIT",
        files);

    [Fact]
    public async Task Installs_a_verified_download_once()
    {
        var body = "engine bytes"u8.ToArray();
        await using var server = await LocalServer.StartAsync(new Dictionary<string, (byte[], string)> { ["/tool.exe"] = (body, "application/octet-stream") });
        using var root = new TempDir();
        using var http = new HttpClient();
        var manager = new EngineManager(root.Path, http);
        var spec = Spec(server.BaseUri, "/tool.exe", body, ["tool.exe"]);

        var path = await manager.EnsureAsync(spec, cancellationToken: TestContext.Current.CancellationToken);
        var again = await manager.EnsureAsync(spec, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(body, File.ReadAllBytes(path));
        Assert.Equal(path, again);
        Assert.Equal(1, server.Requests);
        Assert.Equal(EngineState.Installed, manager.GetStatus(spec).State);
    }

    [Fact]
    public async Task Rejects_a_download_whose_checksum_does_not_match()
    {
        var body = "tampered"u8.ToArray();
        await using var server = await LocalServer.StartAsync(new Dictionary<string, (byte[], string)> { ["/tool.exe"] = (body, "application/octet-stream") });
        using var root = new TempDir();
        using var http = new HttpClient();
        var manager = new EngineManager(root.Path, http);
        var spec = Spec(server.BaseUri, "/tool.exe", "original"u8.ToArray(), ["tool.exe"]) with { SizeBytes = body.Length };

        var error = await Assert.ThrowsAsync<ToolException>(() => manager.EnsureAsync(spec, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("checksum", error.Message, StringComparison.Ordinal);
        Assert.Null(manager.Resolve(spec));
        Assert.Empty(Directory.GetFiles(root.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extracts_only_the_listed_files_from_a_zip()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "pkg/bin/ffmpeg.exe", "pkg/bin/ffprobe.exe", "pkg/doc/readme.txt" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(name);
            }
        }

        var body = zipStream.ToArray();
        await using var server = await LocalServer.StartAsync(new Dictionary<string, (byte[], string)> { ["/pkg.zip"] = (body, "application/zip") });
        using var root = new TempDir();
        using var http = new HttpClient();
        var manager = new EngineManager(root.Path, http);
        var spec = Spec(server.BaseUri, "/pkg.zip", body, ["ffmpeg.exe", "ffprobe.exe"]);

        var main = await manager.EnsureAsync(spec, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("pkg/bin/ffmpeg.exe", File.ReadAllText(main));
        Assert.Equal(["ffmpeg.exe", "ffprobe.exe"], Directory.GetFiles(Path.GetDirectoryName(main)!, "*.exe").Select(Path.GetFileName).Order());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(main)!, "readme.txt")));
    }

    [Fact]
    public async Task A_custom_copy_wins_and_is_never_downloaded_over()
    {
        using var root = new TempDir();
        using var http = new HttpClient();
        var manager = new EngineManager(root.Path, http);
        var custom = Path.Combine(root.Path, "my-ffmpeg.exe");
        File.WriteAllText(custom, "mine");
        var spec = Spec(new Uri("http://127.0.0.1:9/"), "/never", [1], ["ffmpeg.exe"]);

        manager.SetCustomPath(spec.Id, custom);

        Assert.Equal(custom, await manager.EnsureAsync(spec, cancellationToken: TestContext.Current.CancellationToken));
        manager.SetCustomPath(spec.Id, Path.Combine(root.Path, "missing.exe"));
        Assert.Equal(EngineState.CustomMissing, manager.GetStatus(spec).State);
        await Assert.ThrowsAsync<ToolException>(() => manager.EnsureAsync(spec, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Catalog_pins_a_sha256_for_every_engine() =>
        Assert.All(EngineCatalog.All, e => Assert.Matches("^[0-9a-f]{64}$", e.Sha256));
}
