using System.Windows;
using Toolbox.App.Services;
using Toolbox.Core.Settings;

namespace Toolbox.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "Disposed in Quit, the only way the app exits.")]
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppServices _services = null!;
    private MainWindow _main = null!;
    private LauncherWindow _launcher = null!;
    private HotkeyService _hotkey = null!;
    private TrayIcon? _tray;
    private bool _quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = SingleInstance.TryStart(e.Args);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        _services = new AppServices();
        ApplyTheme();
        _main = new MainWindow(_services);
        _main.Closing += (_, args) =>
        {
            if (_services.Settings.KeepRunningInTray && !_quitting)
            {
                args.Cancel = true;
                _main.Hide();
                _tray?.TellStillRunning(_services.Settings.LauncherHotkey);
            }
            else
            {
                Quit();
            }
        };
        _launcher = new LauncherWindow(id => _main.OpenTool(id, []));
        _hotkey = new HotkeyService(_launcher.Summon);
        _tray = new TrayIcon(_main.Summon, _launcher.Summon, Quit);
        RegisterHotkey();
        _services.SettingsChanged += (_, _) =>
        {
            ApplyTheme();
            RegisterHotkey();
        };

        _instance.Listen(args => Dispatcher.BeginInvoke(() => HandleArguments(args, firstLaunch: false)));
        HandleArguments(e.Args, firstLaunch: true);
    }

    private void Quit()
    {
        _quitting = true;
        _tray?.Dispose();
        _hotkey.Dispose();
        _launcher.Close();
        _main.Close();
        _instance?.Dispose();
        Shutdown();
    }

    /// <summary>
    /// Supported command lines: <c>--background</c> (start in the notification area), <c>--launcher</c>, and
    /// <c>--tool &lt;id&gt; [files or folders…]</c> to open a tool with its inputs filled in.
    /// </summary>
    private void HandleArguments(string[] args, bool firstLaunch)
    {
        var toolIndex = Array.FindIndex(args, a => a.Equals("--tool", StringComparison.OrdinalIgnoreCase));
        if (toolIndex >= 0 && toolIndex + 1 < args.Length)
        {
            _main.OpenTool(args[toolIndex + 1], args[(toolIndex + 2)..]);
            _main.Summon();
        }
        else if (args.Contains("--launcher", StringComparer.OrdinalIgnoreCase))
        {
            _launcher.Summon();
        }
        else if (!(firstLaunch && args.Contains("--background", StringComparer.OrdinalIgnoreCase)))
        {
            _main.Summon();
        }
    }

    private void RegisterHotkey() => _main.HotkeyRegistered = _hotkey.Register(_services.Settings.LauncherHotkey);

    private void ApplyTheme() => ThemeMode = _services.Settings.Theme switch
    {
        AppTheme.Light => ThemeMode.Light,
        AppTheme.Dark => ThemeMode.Dark,
        _ => ThemeMode.System,
    };
}
