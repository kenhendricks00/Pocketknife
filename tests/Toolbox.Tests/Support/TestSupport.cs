using System.Security.Cryptography;
using Toolbox.Core.Engines;
using Toolbox.Core.Running;
using Toolbox.Core.Tools;

[assembly: AssemblyFixture(typeof(Toolbox.Tests.Support.Engines))]

namespace Toolbox.Tests.Support;

public static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

/// <summary>
/// Real engines, downloaded once per machine through <see cref="EngineManager"/> (so the download and checksum
/// path is exercised too) into a cache folder that CI keeps between runs.
/// </summary>
public sealed class Engines : IDisposable
{
    public Engines()
    {
        var root = Environment.GetEnvironmentVariable("TOOLBOX_TEST_ENGINES")
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsToolbox-tests", "engines");
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsToolbox-tests/1.0");
        Http.Timeout = TimeSpan.FromMinutes(20);
        Manager = new EngineManager(root, Http);
    }

    public HttpClient Http { get; } = new();

    public EngineManager Manager { get; }

    public void Dispose() => Http.Dispose();
}

/// <summary>A temporary folder that is deleted afterwards.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "toolbox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Copy(string fixture)
    {
        var target = System.IO.Path.Combine(Path, System.IO.Path.GetFileName(fixture));
        File.Copy(fixture, target);
        return target;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class ToolHarness
{
    /// <summary>
    /// Runs a tool the way the app does and checks the shared promises: inputs are byte-for-byte unchanged and every
    /// output lands in one new dated folder under the output root.
    /// </summary>
    public static async Task<RunResult> RunAsync(
        Engines engines,
        Tool tool,
        IReadOnlyList<string> paths,
        Dictionary<string, object?>? options = null,
        string text = "")
    {
        using var outputRoot = new TempDir();
        var inputs = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*", SearchOption.AllDirectories) : [p]).ToList();
        var before = inputs.ToDictionary(p => p, Hash);

        var runner = new ToolRunner(engines.Manager, () => outputRoot.Path);
        var result = await runner.RunAsync(tool, new RunRequest(paths, text, options), cancellationToken: TestContext.Current.CancellationToken);

        foreach (var (path, hash) in before)
        {
            Assert.True(File.Exists(path), $"Input {path} was removed.");
            Assert.Equal(hash, Hash(path));
        }

        foreach (var input in paths.Where(Directory.Exists))
        {
            Assert.Equal(before.Keys.Where(k => k.StartsWith(input, StringComparison.OrdinalIgnoreCase)).Order(), Directory.GetFiles(input, "*", SearchOption.AllDirectories).Order());
        }

        if (result.OutputDirectory is not null)
        {
            Assert.Equal(outputRoot.Path, System.IO.Path.GetDirectoryName(result.OutputDirectory));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}\.\d{2}\.\d{2} ", System.IO.Path.GetFileName(result.OutputDirectory));
        }

        var keep = Path.Combine(System.IO.Path.GetTempPath(), "toolbox-tests", "outputs", tool.Id, Guid.NewGuid().ToString("N")[..8]);
        var copied = new List<string>();
        foreach (var output in result.Outputs)
        {
            Assert.True(new FileInfo(output).Length > 0, $"{output} is empty.");
            Directory.CreateDirectory(keep);
            var target = System.IO.Path.Combine(keep, System.IO.Path.GetFileName(output));
            File.Copy(output, target);
            copied.Add(target);
        }

        return result with { OutputDirectory = copied.Count > 0 ? keep : null, Outputs = copied };
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
