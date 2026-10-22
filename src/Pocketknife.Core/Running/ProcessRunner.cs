using System.Diagnostics;
using System.Text;

namespace Pocketknife.Core.Running;

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> ErrorTail);

public static class ProcessRunner
{
    private const int TailLines = 15;

    /// <summary>Runs an executable without a window, streaming its output lines. Cancelling kills the whole process tree.</summary>
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var tail = new Queue<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutput?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (tail)
            {
                tail.Enqueue(e.Data);
                if (tail.Count > TailLines)
                {
                    tail.Dequeue();
                }
            }

            onError?.Invoke(e.Data);
        };

        if (!process.Start())
        {
            throw new ToolException($"Could not start {Path.GetFileName(executable)}.");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        lock (tail)
        {
            return new ProcessResult(process.ExitCode, [.. tail]);
        }
    }

    public static ProcessResult EnsureSuccess(this ProcessResult result, string what)
    {
        if (result.ExitCode != 0)
        {
            var detail = result.ErrorTail.LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? $"exit code {result.ExitCode}";
            throw new ToolException($"{what} failed: {detail}");
        }

        return result;
    }
}
