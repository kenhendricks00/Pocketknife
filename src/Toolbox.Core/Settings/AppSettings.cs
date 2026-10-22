using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolbox.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    /// <summary>Where dated run folders are created. Null means the default under Downloads.</summary>
    public string? OutputRoot { get; init; }

    public AppTheme Theme { get; init; } = AppTheme.System;

    public string LauncherHotkey { get; init; } = "Ctrl+Alt+Space";

    public bool KeepRunningInTray { get; init; } = true;

    public Dictionary<string, string> EnginePaths { get; init; } = [];

    public string EffectiveOutputRoot => string.IsNullOrWhiteSpace(OutputRoot) ? AppPaths.DefaultOutputRoot : OutputRoot;
}

public static partial class AppPaths
{
    public const string ProductName = "Windows Toolbox";
    public const string FolderName = "WindowsToolbox";

    private static readonly Guid s_downloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string DataRoot { get; } =
        Environment.GetEnvironmentVariable("WINDOWS_TOOLBOX_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static string EnginesRoot => Path.Combine(DataRoot, "engines");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public static string DefaultOutputRoot => Path.Combine(DownloadsFolder, ProductName);

    /// <summary>The Downloads known folder, which people can move away from the user profile.</summary>
    public static string DownloadsFolder
    {
        get
        {
            if (SHGetKnownFolderPath(s_downloadsFolder, 0, IntPtr.Zero, out var pointer) == 0)
            {
                try
                {
                    return Marshal.PtrToStringUni(pointer)!;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), s_json) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, s_json));
        File.Move(temp, path, overwrite: true);
    }
}
