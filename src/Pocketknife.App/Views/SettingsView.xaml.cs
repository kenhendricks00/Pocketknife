using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Pocketknife.App.Services;
using Pocketknife.App.ViewModels;
using Pocketknife.Core.Engines;
using Pocketknife.Core.Launcher;
using Pocketknife.Core.Settings;

namespace Pocketknife.App.Views;

public partial class SettingsView : UserControl
{
    private readonly AppServices _services;
    private readonly LauncherHotkey _hotkey;
    private readonly bool _loading;
    private bool _capturing;
    private HotkeyGesture? _pending;

    public SettingsView(AppServices services, LauncherHotkey hotkey)
    {
        _services = services;
        _hotkey = hotkey;
        _loading = true;
        InitializeComponent();
        var settings = services.Settings;
        OutputPath.Text = settings.EffectiveOutputRoot;
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == settings.Theme.ToString());
        TrayBox.IsChecked = settings.KeepRunningInTray;
        StartupBox.IsChecked = StartWithWindows.IsEnabled;
        EnginesPath.Text = services.Engines.Root;
        EngineList.ItemsSource = EngineCatalog.All.Select(e => new EngineRowViewModel(e, services)).ToList();
        VersionText.Text = $"Pocketknife {typeof(SettingsView).Assembly.GetName().Version?.ToString(3)} · A PC multitool for Windows · MIT License · Copyright (c) 2026 Kenneth Hendricks";

        WebEnabledBox.IsChecked = settings.WebSearch.Enabled;
        foreach (var engine in SearchEngines.BuiltIn)
        {
            EngineBox.Items.Add(new ComboBoxItem { Content = engine.Name, Tag = engine.Id });
        }

        EngineBox.Items.Add(new ComboBoxItem { Content = "Custom address…", Tag = SearchEngines.CustomId });
        CustomTemplateBox.Text = settings.WebSearch.CustomTemplate;
        CalculatorBox.IsChecked = settings.CalculatorEnabled;
        ShowWebSettings();
        _loading = false;
        ShowHotkey();
        if (!_hotkey.IsRegistered)
        {
            ShowMessage(HotkeyMessage, $"{_hotkey.Current} is used by another app, so the launcher only opens from the notification-area icon. Choose a different shortcut.", Tone.Error);
        }
    }

    private enum Tone
    {
        Info,
        Warning,
        Error,
    }

    private static void ShowMessage(TextBlock target, string text, Tone tone)
    {
        target.Text = text;
        target.SetResourceReference(TextBlock.ForegroundProperty, tone switch
        {
            Tone.Error => "SystemFillColorCriticalBrush",
            Tone.Warning => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        });
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

    // Launcher shortcut: "Change…" releases the current shortcut and the next key press with Ctrl, Alt or Win becomes the new one.
    private void ShowHotkey() => HotkeyCapture.Text = _hotkey.Current.ToString();

    internal void OnChangeHotkey(object sender, RoutedEventArgs e)
    {
        HotkeyConfirm.Visibility = Visibility.Collapsed;
        _pending = null;
        _capturing = true;
        _hotkey.Suspend();
        HotkeyCapture.Text = "Press the new shortcut…";
        ShowMessage(HotkeyMessage, "Hold Ctrl, Alt or Win and press a key. Esc cancels.", Tone.Info);
        HotkeyCapture.Focus();
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        HandleCapturedKey(key, Keyboard.Modifiers);
    }

    /// <summary>One key press while capturing a new shortcut, with the modifiers held at the time.</summary>
    internal void HandleCapturedKey(Key key, ModifierKeys modifierKeys)
    {
        if (!_capturing)
        {
            return;
        }

        var modifiers = ToModifiers(modifierKeys);
        if (key == Key.Escape && modifiers == HotkeyModifiers.None)
        {
            StopCapture();
            ShowMessage(HotkeyMessage, "", Tone.Info);
            return;
        }

        if (HotkeyGesture.IsModifierKey(key.ToString()))
        {
            HotkeyCapture.Text = modifiers == HotkeyModifiers.None ? "Press the new shortcut…" : $"{new HotkeyGesture(modifiers, "…")}";
            return;
        }

        StopCapture();
        Consider(new HotkeyGesture(modifiers, key.ToString()));
    }

    private void OnHotkeyCaptureLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturing)
        {
            StopCapture();
            ShowMessage(HotkeyMessage, "", Tone.Info);
        }
    }

    private void StopCapture()
    {
        _capturing = false;
        _hotkey.Register();
        ShowHotkey();
    }

    private void Consider(HotkeyGesture gesture)
    {
        var verdict = HotkeyAdvice.Check(gesture);
        switch (verdict.Level)
        {
            case HotkeyVerdictLevel.Reject:
                ShowMessage(HotkeyMessage, $"{gesture} can't be used. {verdict.Message}", Tone.Error);
                break;
            case HotkeyVerdictLevel.Warn:
                _pending = gesture;
                HotkeyCapture.Text = gesture.ToString();
                ShowMessage(HotkeyMessage, $"{verdict.Message} Use {gesture} anyway?", Tone.Warning);
                HotkeyConfirm.Visibility = Visibility.Visible;
                break;
            default:
                Apply(gesture);
                break;
        }
    }

    private void Apply(HotkeyGesture gesture)
    {
        HotkeyConfirm.Visibility = Visibility.Collapsed;
        _pending = null;
        var error = _hotkey.TryChange(gesture);
        ShowHotkey();
        if (error is null)
        {
            ShowMessage(HotkeyMessage, $"Saved. Press {gesture} anywhere to open the launcher.", Tone.Info);
        }
        else
        {
            ShowMessage(HotkeyMessage, error, Tone.Error);
        }
    }

    internal void OnUseHotkeyAnyway(object sender, RoutedEventArgs e)
    {
        if (_pending is { } gesture)
        {
            Apply(gesture);
        }
    }

    internal void OnKeepHotkey(object sender, RoutedEventArgs e)
    {
        HotkeyConfirm.Visibility = Visibility.Collapsed;
        _pending = null;
        ShowHotkey();
        ShowMessage(HotkeyMessage, "", Tone.Info);
    }

    internal void OnResetHotkey(object sender, RoutedEventArgs e) => Apply(LauncherHotkey.Default);

    private static HotkeyModifiers ToModifiers(ModifierKeys keys) =>
        (keys.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Ctrl : 0)
        | (keys.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0)
        | (keys.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0)
        | (keys.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Win : 0);

    // Web search and bangs.
    private void ShowWebSettings()
    {
        var web = _services.Settings.WebSearch;
        WebOptions.IsEnabled = web.Enabled;
        EngineBox.SelectedItem = EngineBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == web.Engine)
            ?? EngineBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == SearchEngines.DefaultId);
        CustomEnginePanel.Visibility = web.Engine == SearchEngines.CustomId ? Visibility.Visible : Visibility.Collapsed;
        BangList.ItemsSource = Bangs.Effective(web).OrderBy(b => b.Trigger, StringComparer.Ordinal).ToList();
    }

    private void UpdateWeb(Func<WebSearchSettings, WebSearchSettings> change)
    {
        _services.Update(s => s with { WebSearch = change(s.WebSearch) });
        ShowWebSettings();
    }

    private void OnWebEnabledChanged(object sender, RoutedEventArgs e) =>
        UpdateWeb(w => w with { Enabled = WebEnabledBox.IsChecked == true });

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EngineBox.SelectedItem is not ComboBoxItem { Tag: string id } || id == _services.Settings.WebSearch.Engine)
        {
            return;
        }

        if (id != SearchEngines.CustomId)
        {
            UpdateWeb(w => w with { Engine = id });
            return;
        }

        CustomEnginePanel.Visibility = Visibility.Visible;
        if (UrlTemplate.TryCreate(CustomTemplateBox.Text, out _, out _))
        {
            UseCustomEngine();
        }
        else
        {
            ShowMessage(CustomEngineMessage, "Enter a search address with {query} where the words go, then choose Use.", Tone.Info);
            CustomTemplateBox.Focus();
        }
    }

    internal void OnUseCustomEngine(object sender, RoutedEventArgs e) => UseCustomEngine();

    private void UseCustomEngine()
    {
        if (!UrlTemplate.TryCreate(CustomTemplateBox.Text, out var template, out var error))
        {
            ShowMessage(CustomEngineMessage, error, Tone.Error);
            return;
        }

        UpdateWeb(w => w with { Engine = SearchEngines.CustomId, CustomTemplate = template!.Pattern });
        ShowMessage(CustomEngineMessage, $"Searches now go to {template!.Home.Host}.", Tone.Info);
    }

    internal void OnAddBang(object sender, RoutedEventArgs e)
    {
        var bang = new Bang(NewBangTrigger.Text, NewBangName.Text, NewBangTemplate.Text);
        if (!Bangs.TryValidate(bang, out var error))
        {
            ShowMessage(BangMessage, error, Tone.Error);
            return;
        }

        UpdateWeb(w => Bangs.Add(w, bang));
        ShowMessage(BangMessage, $"Added !{bang.Trigger.Trim().TrimStart('!').ToLowerInvariant()}.", Tone.Info);
        NewBangTrigger.Text = NewBangName.Text = NewBangTemplate.Text = "";
    }

    internal void OnRemoveBang(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string trigger })
        {
            UpdateWeb(w => Bangs.Remove(w, trigger));
            ShowMessage(BangMessage, $"Removed !{trigger}.", Tone.Info);
        }
    }

    internal void OnRestoreBangs(object sender, RoutedEventArgs e)
    {
        UpdateWeb(Bangs.RestoreDefaults);
        ShowMessage(BangMessage, "Default bangs restored.", Tone.Info);
    }

    private void OnCalculatorChanged(object sender, RoutedEventArgs e) =>
        _services.Update(s => s with { CalculatorEnabled = CalculatorBox.IsChecked == true });

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
