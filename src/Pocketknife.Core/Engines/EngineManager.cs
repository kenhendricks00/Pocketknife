using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Engines;

public enum EngineState
{
    NotInstalled,
    Installed,
    Custom,
    CustomMissing,
}

public sealed record EngineStatus(EngineState State, string? Path);

/// <summary>
/// Installs engines into <c>root\{id}\{version}</c>. Downloads are hashed while streaming and discarded unless the
/// SHA-256 matches the pinned value. A custom path set by the person always wins over the managed copy.
/// </summary>
public sealed class EngineManager(string root, HttpClient http)
{
    private const string StampFile = ".verified";
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, string> _customPaths = new();

    public string Root { get; } = root;

    public void SetCustomPath(string engineId, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _customPaths.TryRemove(engineId, out _);
        }
        else
        {
            _customPaths[engineId] = path;
        }
    }

    public EngineStatus GetStatus(EngineSpec spec)
    {
        if (_customPaths.TryGetValue(spec.Id, out var custom))
        {
            return File.Exists(custom) ? new(EngineState.Custom, custom) : new(EngineState.CustomMissing, custom);
        }

        var main = Path.Combine(InstallDirectory(spec), spec.MainFile);
        return IsVerifiedInstall(spec) && File.Exists(main) ? new(EngineState.Installed, main) : new(EngineState.NotInstalled, null);
    }

    /// <summary>Path of the engine's main file, or null when it is not available yet.</summary>
    public string? Resolve(EngineSpec spec)
    {
        var status = GetStatus(spec);
        return status.State is EngineState.Installed or EngineState.Custom ? status.Path : null;
    }

    public string Require(EngineSpec spec) =>
        Resolve(spec) ?? throw new ToolException($"{spec.Name} is not installed. Run the tool again to download it, or choose your own copy in Settings.");

    /// <summary>Path of a companion file (for example ffprobe.exe) next to the engine's main file.</summary>
    public string RequireFile(EngineSpec spec, string fileName)
    {
        var path = Path.Combine(Path.GetDirectoryName(Require(spec))!, fileName);
        return File.Exists(path) ? path : throw new ToolException($"{fileName} was not found next to {spec.MainFile}.");
    }

    public async Task<string> EnsureAsync(EngineSpec spec, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(spec.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (GetStatus(spec) is { State: EngineState.CustomMissing, Path: var missing })
            {
                throw new ToolException($"Your {spec.Name} copy was not found at {missing}. Fix the path in Settings.");
            }

            if (Resolve(spec) is { } existing)
            {
                return existing;
            }

            await InstallAsync(spec, progress, cancellationToken);
            return Resolve(spec) ?? throw new ToolException($"{spec.Name} installed but {spec.MainFile} is missing.");
        }
        finally
        {
            gate.Release();
        }
    }

    public void Remove(EngineSpec spec)
    {
        var directory = Path.Combine(Root, spec.Id);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string InstallDirectory(EngineSpec spec) => Path.Combine(Root, spec.Id, spec.Version);

    private bool IsVerifiedInstall(EngineSpec spec)
    {
        var stamp = Path.Combine(InstallDirectory(spec), StampFile);
        return File.Exists(stamp) && File.ReadAllText(stamp).Trim() == spec.Sha256;
    }

    private async Task InstallAsync(EngineSpec spec, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(Root, spec.Id, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var download = Path.Combine(staging, "download.part");
            await DownloadVerifiedAsync(spec, download, progress, cancellationToken);

            if (spec.IsZip)
            {
                ExtractFiles(spec, download, staging);
            }
            else
            {
                File.Move(download, Path.Combine(staging, spec.MainFile));
            }

            File.Delete(download);
            await File.WriteAllTextAsync(Path.Combine(staging, StampFile), spec.Sha256, cancellationToken);

            var target = InstallDirectory(spec);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(staging, target);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task DownloadVerifiedAsync(EngineSpec spec, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(spec.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ToolException($"Could not download {spec.Name} ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(destination))
        {
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress?.Report(Math.Min(1, (double)total / spec.SizeBytes));
            }
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!string.Equals(actual, spec.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new ToolException($"The {spec.Name} download did not match its expected checksum, so it was discarded. Expected {spec.Sha256}, got {actual}.");
        }
    }

    private static void ExtractFiles(EngineSpec spec, string zipPath, string directory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var name in spec.Files)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ToolException($"{name} was not found in the {spec.Name} download.");
            entry.ExtractToFile(Path.Combine(directory, name));
        }
    }
}
