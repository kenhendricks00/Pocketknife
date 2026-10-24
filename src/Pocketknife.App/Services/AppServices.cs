using System.Globalization;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Launcher;
using Pocketknife.Core.Running;
using Pocketknife.Core.Settings;

namespace Pocketknife.App.Services;

/// <summary>The app's long-lived services, created once at startup.</summary>
public sealed class AppServices
{
    private readonly SettingsStore _store;

    /// <param name="dataRoot">Where settings and engines live; the user's app-data folder unless a test passes its own.</param>
    public AppServices(string? dataRoot = null)
    {
        _store = new SettingsStore(dataRoot is null ? AppPaths.SettingsFile : Path.Combine(dataRoot, "settings.json"));
        Settings = _store.Load();
        var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Pocketknife/{typeof(AppServices).Assembly.GetName().Version?.ToString(3)}");
        Engines = new EngineManager(dataRoot is null ? AppPaths.EnginesRoot : Path.Combine(dataRoot, "engines"), http);
        foreach (var (id, path) in Settings.EnginePaths)
        {
            Engines.SetCustomPath(id, path);
        }

        Runner = new ToolRunner(Engines, () => Settings.EffectiveOutputRoot);
    }

    public AppSettings Settings { get; private set; }

    public EngineManager Engines { get; }

    public ToolRunner Runner { get; }

    /// <summary>What the launcher offers besides tools, read fresh so settings changes apply right away.</summary>
    public LauncherOptions LauncherOptions =>
        new(WebSearch.From(Settings.WebSearch), Settings.CalculatorEnabled, CultureInfo.CurrentCulture);

    public event EventHandler? SettingsChanged;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        _store.Save(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEnginePath(EngineSpec engine, string? path)
    {
        Engines.SetCustomPath(engine.Id, path);
        Update(s =>
        {
            var paths = new Dictionary<string, string>(s.EnginePaths);
            if (string.IsNullOrWhiteSpace(path))
            {
                paths.Remove(engine.Id);
            }
            else
            {
                paths[engine.Id] = path;
            }

            return s with { EnginePaths = paths };
        });
    }
}
