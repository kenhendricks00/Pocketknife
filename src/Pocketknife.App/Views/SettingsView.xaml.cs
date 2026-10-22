using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pocketknife.App.Services;
using Pocketknife.App.ViewModels;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Settings;

namespace Pocketknife.App.Views;

public partial class SettingsView : UserControl
{
    private readonly AppServices _services;
    private readonly Func<bool> _hotkeyRegistered;
    private readonly bool _loading;

    public SettingsView(AppServices services, Func<bool> hotkeyRegistered)
    {
        _services = services;
        _hotkeyRegistered = hotkeyRegistered;
        _loading = true;
        InitializeComponent();
        var settings = services.Settings;
        OutputPath.Text = settings.EffectiveOutputRoot;
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == settings.Theme.ToString());
        HotkeyBox.ItemsSource = HotkeyService.Choices.Contains(settings.LauncherHotkey)
            ? HotkeyService.Choices
            : [settings.LauncherHotkey, .. HotkeyService.Choices];
        HotkeyBox.SelectedItem = settings.LauncherHotkey;
        TrayBox.IsChecked = settings.KeepRunningInTray;
        StartupBox.IsChecked = StartWithWindows.IsEnabled;
        EnginesPath.Text = AppPaths.EnginesRoot;
        EngineList.ItemsSource = EngineCatalog.All.Select(e => new EngineRowViewModel(e, services)).ToList();
        VersionText.Text = $"Pocketknife {typeof(SettingsView).Assembly.GetName().Version?.ToString(3)} · A PC multitool for Windows · MIT License · Copyright (c) 2026 Kenneth Hendricks";
        _loading = false;
        ShowHotkeyState();
    }

    private void OnChangeOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where results are saved", InitialDirectory = _services.Settings.EffectiveOutputRoot };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _services.Update(s => s with { OutputRoot = dialog.FolderName });
            OutputPath.Text = _services.Settings.EffectiveOutputRoot;
        }
    }

    private void OnResetOutput(object sender, RoutedEventArgs e)
    {
        _services.Update(s => s with { OutputRoot = null });
        OutputPath.Text = _services.Settings.EffectiveOutputRoot;
    }

    private void OnOpenOutput(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_services.Settings.EffectiveOutputRoot);
        ToolViewModel.OpenFolder(_services.Settings.EffectiveOutputRoot);
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _services.Update(s => s with { Theme = Enum.Parse<AppTheme>(tag) });
        }
    }

    private void OnHotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && HotkeyBox.SelectedItem is string hotkey)
        {
            _services.Update(s => s with { LauncherHotkey = hotkey });
            ShowHotkeyState();
        }
    }

    private void ShowHotkeyState() =>
        HotkeyWarning.Text = _hotkeyRegistered() ? "" : "Another app is using this shortcut. Pick a different one.";

    private void OnTrayChanged(object sender, RoutedEventArgs e) =>
        _services.Update(s => s with { KeepRunningInTray = TrayBox.IsChecked == true });

    private void OnStartupChanged(object sender, RoutedEventArgs e) => StartWithWindows.Set(StartupBox.IsChecked == true);

    private void OnLicense(object sender, RoutedEventArgs e) => OpenBesideApp("LICENSE.txt");

    private void OnNotices(object sender, RoutedEventArgs e) => OpenBesideApp("THIRD-PARTY-NOTICES.md");

    private static void OpenBesideApp(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path } })?.Dispose();
        }
    }
}
