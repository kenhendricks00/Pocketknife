using Pocketknife.Core.Engines;
using Pocketknife.Core.Tools;

namespace Pocketknife.Core.Running;

public sealed record RunRequest(
    IReadOnlyList<string> Paths,
    string Text = "",
    IReadOnlyDictionary<string, object?>? Options = null);

public sealed record RunProgress(string Status, double? Fraction);

public sealed record RunFailure(string Item, string Message);

public sealed record RunResult(
    string? OutputDirectory,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<RunFailure> Failures,
    IReadOnlyList<string> Messages);

/// <summary>
/// Runs a tool: expands inputs, makes sure engines are installed, creates a new dated output folder and
/// runs one job per input. A failing input is reported without stopping the rest of the batch.
/// </summary>
public sealed class ToolRunner(EngineManager engines, Func<string> outputRoot, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<RunResult> RunAsync(
        Tool tool,
        RunRequest request,
        IProgress<RunProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= new Progress<RunProgress>();
        log ??= _ => { };
        var options = new OptionValues(tool.Options, request.Options);
        var failures = new List<RunFailure>();
        var items = PlanItems(tool.Input, request, failures);

        foreach (var engine in tool.RequiredEngines(options))
        {
            if (engines.Resolve(engine) is null)
            {
                progress.Report(new RunProgress($"Downloading {engine.Name} (first use only)…", 0));
                var download = new Progress<double>(f => progress.Report(new RunProgress($"Downloading {engine.Name} (first use only)…", f)));
                await engines.EnsureAsync(engine, download, cancellationToken);
                log($"Installed {engine.Name} {engine.Version}.");
            }
        }

        var outputDirectory = OutputNames.CreateRunDirectory(outputRoot(), tool.Name, _time.GetLocalNow());
        var messages = new List<string>();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var index = i;
                var status = items.Count == 1 ? $"Working on {item.Label}…" : $"Working on {item.Label} ({i + 1} of {items.Count})…";
                progress.Report(new RunProgress(status, (double)i / items.Count));
                var jobProgress = new Progress<double>(f =>
                    progress.Report(new RunProgress(status, (index + Math.Clamp(f, 0, 1)) / items.Count)));
                var job = new ToolJob(item.Files, item.Text, options, outputDirectory, engines, jobProgress, log, messages.Add);
                var failed = true;
                try
                {
                    await tool.RunAsync(job, cancellationToken);
                    failed = false;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log($"{item.Label}: {ex}");
                    failures.Add(new RunFailure(item.Label, ex is ToolException or ArgumentException ? ex.Message : $"Unexpected error: {ex.Message}"));
                }
                finally
                {
                    job.CleanUp(failed);
                }
            }
        }
        finally
        {
            if (!Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                Directory.Delete(outputDirectory);
            }
        }

        progress.Report(new RunProgress("Done", 1));
        var outputs = Directory.Exists(outputDirectory)
            ? Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        return new RunResult(Directory.Exists(outputDirectory) ? outputDirectory : null, outputs, failures, messages);
    }

    private sealed record WorkItem(string Label, IReadOnlyList<string> Files, string Text);

    private static List<WorkItem> PlanItems(ToolInput input, RunRequest request, List<RunFailure> failures)
    {
        switch (input)
        {
            case FileInput fileInput:
                var files = ExpandFiles(fileInput, request.Paths, failures);
                if (files.Count == 0)
                {
                    throw new ToolException($"No {fileInput.Noun} to work on. Add a file or a folder that contains {fileInput.Noun}.");
                }

                if (fileInput.Combine)
                {
                    if (files.Count < fileInput.MinimumFiles)
                    {
                        throw new ToolException($"Add at least {fileInput.MinimumFiles} {fileInput.Noun}.");
                    }

                    var label = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files";
                    return [new WorkItem(label, files, "")];
                }

                return [.. files.Select(f => new WorkItem(Path.GetFileName(f), [f], ""))];

            case TextInput textInput:
                var text = request.Text.Trim();
                if (text.Length == 0)
                {
                    throw new ToolException($"Enter {textInput.Label.ToLowerInvariant()} first.");
                }

                return textInput.SplitLines
                    ? [.. text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => new WorkItem(l, [], l))]
                    : [new WorkItem("text", [], text)];

            default:
                throw new NotSupportedException(input.GetType().Name);
        }
    }

    /// <summary>Files are kept in the order given; a folder contributes its top-level matching files sorted by name.</summary>
    internal static List<string> ExpandFiles(FileInput input, IReadOnlyList<string> paths, List<RunFailure> failures)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                result.AddRange(Directory.EnumerateFiles(path)
                    .Where(input.Accepts)
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(path))
            {
                if (input.Accepts(path))
                {
                    result.Add(Path.GetFullPath(path));
                }
                else
                {
                    failures.Add(new RunFailure(Path.GetFileName(path), $"Not a supported file type. Accepted: {string.Join(", ", input.Extensions)}."));
                }
            }
            else
            {
                failures.Add(new RunFailure(path, "File or folder not found."));
            }
        }

        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
