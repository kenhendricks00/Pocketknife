using System.Globalization;
using System.Text;

namespace Pocketknife.Core.Running;

public static class OutputNames
{
    private static readonly HashSet<char> s_invalid = [.. Path.GetInvalidFileNameChars()];
    private static readonly Lock s_gate = new();

    public static string Sanitize(string name, int maxLength = 120)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(s_invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        }

        var cleaned = builder.ToString().Trim().TrimEnd('.');
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].TrimEnd();
        }

        return cleaned.Length == 0 ? "output" : cleaned;
    }

    /// <summary>
    /// Returns a path in <paramref name="directory"/> that does not exist yet, adding " (2)", " (3)", ... as needed.
    /// The name is claimed with an empty placeholder file so parallel callers never receive the same path.
    /// </summary>
    public static string Unique(string directory, string fileName)
    {
        var stem = Sanitize(Path.GetFileNameWithoutExtension(fileName));
        var extension = Path.GetExtension(fileName);
        lock (s_gate)
        {
            for (var n = 1; ; n++)
            {
                var candidate = Path.Combine(directory, n == 1 ? stem + extension : $"{stem} ({n}){extension}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    using (File.Create(candidate))
                    {
                    }

                    return candidate;
                }
            }
        }
    }

    /// <summary>Creates a new dated folder such as "2026-10-21 16.30.05 Compress Video" under <paramref name="root"/>.</summary>
    public static string CreateRunDirectory(string root, string toolName, DateTimeOffset now)
    {
        var stamp = now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
        var baseName = Sanitize($"{stamp} {toolName}");
        lock (s_gate)
        {
            for (var n = 1; ; n++)
            {
                var candidate = Path.Combine(root, n == 1 ? baseName : $"{baseName} ({n})");
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                {
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
            }
        }
    }
}
