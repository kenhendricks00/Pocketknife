using Pocketknife.Core.Engines;
using Pocketknife.Core.Running;
using Pocketknife.Core.Settings;

namespace Pocketknife.App.Services;

/// <summary>The app's long-lived services, created once at startup.</summary>
public sealed class AppServices
{
    private readonly SettingsStore _store = new(AppPaths.SettingsFile);

    public AppServices()
    {
        Settings = _store.Load();
        var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Pocketknife/{typeof(AppServices).Assembly.GetName().Version?.ToString(3)}");
        Engines = new EngineManager(AppPaths.EnginesRoot, http);
        foreach (var (id, path) in Settings.EnginePaths)
        {
            Engines.SetCustomPath(id, path);
        }

        Runner = new ToolRunner(Engines, () => Settings.EffectiveOutputRoot);
    }

    public AppSettings Settings { get; private set; }

    public EngineManager Engines { get; }

    public ToolRunner Runner { get; }

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
