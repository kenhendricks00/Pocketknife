using Pocketknife.Core.Engines;
using Pocketknife.Core.Tools;

namespace Pocketknife.Core.Running;

/// <summary>An error whose message is meant for the person running the tool.</summary>
public sealed class ToolException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One unit of work: a single input file (or all files for combining tools) or one piece of text.</summary>
public sealed class ToolJob
{
    private readonly Action<string> _log;
    private readonly Action<string> _report;
    private readonly List<string> _reserved = [];

    internal ToolJob(
        IReadOnlyList<string> files,
        string text,
        OptionValues options,
        string outputDirectory,
        EngineManager engines,
        IProgress<double> progress,
        Action<string> log,
        Action<string> report)
    {
        Files = files;
        Text = text;
        Options = options;
        OutputDirectory = outputDirectory;
        Engines = engines;
        Progress = progress;
        _log = log;
        _report = report;
    }

    public IReadOnlyList<string> Files { get; }

    public string File => Files.Count == 1 ? Files[0] : throw new InvalidOperationException("This job has several files.");

    public string Text { get; }

    public OptionValues Options { get; }

    public string OutputDirectory { get; }

    public EngineManager Engines { get; }

    public IProgress<double> Progress { get; }

    /// <summary>Returns an unused path in the output folder for <paramref name="fileName"/>.</summary>
    public string OutputPath(string fileName)
    {
        var path = OutputNames.Unique(OutputDirectory, fileName);
        _reserved.Add(path);
        return path;
    }

    /// <summary>Returns an unused output path named after <paramref name="input"/> with a new extension.</summary>
    public string OutputPathFor(string input, string extension, string? suffix = null) =>
        OutputPath(Path.GetFileNameWithoutExtension(input) + suffix + extension);

    public void Log(string line) => _log(line);

    /// <summary>A short result shown to the person, such as the text read from a QR code.</summary>
    public void Report(string message) => _report(message);

    /// <summary>Deletes reserved outputs that were never written, and every reserved output if the job failed.</summary>
    internal void CleanUp(bool failed)
    {
        foreach (var path in _reserved)
        {
            var info = new FileInfo(path);
            if (info.Exists && (failed || info.Length == 0))
            {
                info.Delete();
            }
        }
    }
}
