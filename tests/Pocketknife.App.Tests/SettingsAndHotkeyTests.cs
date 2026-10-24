using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pocketknife.App.Services;
using Pocketknife.App.Views;
using Pocketknife.Core.Launcher;

namespace Pocketknife.App.Tests;

/// <summary>
/// The global hotkey, tested only with F2x shortcuts nobody can press by accident, and triggered by posting
/// WM_HOTKEY to the app's own message window rather than by pressing keys.
/// </summary>
public partial class HotkeyServiceTests(UiThread ui)
{
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private static HotkeyGesture Gesture(string text) => HotkeyGesture.TryParse(text, out var g) ? g! : throw new ArgumentException(text);

    [Fact]
    public void A_registered_hotkey_calls_back_when_windows_reports_it() => ui.Run(() =>
    {
        var pressed = 0;
        using var service = new HotkeyService(() => pressed++);

        Assert.True(service.Register(Gesture("Ctrl+Alt+Shift+F24")));
        SendMessage(service.Handle, 0x0312, 0x5442, IntPtr.Zero);

        Assert.Equal(1, pressed);
    });

    [Fact]
    public void A_shortcut_held_by_someone_else_is_refused_and_freed_on_dispose() => ui.Run(() =>
    {
        using var other = new HotkeyService(() => { });
        Assert.True(other.Register(Gesture("Ctrl+Alt+Shift+F23")));
        using var service = new HotkeyService(() => { });

        Assert.False(service.Register(Gesture("Ctrl+Alt+Shift+F23")));
        other.Dispose();
        Assert.True(service.Register(Gesture("Ctrl+Alt+Shift+F23")));
    });

    [Fact]
    public void Changing_to_a_taken_shortcut_keeps_the_old_one() => ui.Run(() =>
    {
        using var data = new TestData();
        var services = data.Services();
        using var blocker = new HotkeyService(() => { });
        Assert.True(blocker.Register(Gesture("Ctrl+Alt+Shift+F22")));
        using var service = new HotkeyService(() => { });
        var hotkey = new LauncherHotkey(service, services);
        Assert.True(hotkey.Register());

        var error = hotkey.TryChange(Gesture("Ctrl+Alt+Shift+F22"));

        Assert.Contains("already used by another app", error, StringComparison.Ordinal);
        Assert.Equal(TestData.HarmlessHotkey, data.SavedSettings.LauncherHotkey);
        Assert.True(hotkey.IsRegistered);
    });
}

/// <summary>The settings screen, laid out and rendered off-screen; key presses are fed to the capture logic directly.</summary>
public class SettingsViewTests(UiThread ui)
{
    private sealed class Screen : IDisposable
    {
        public Screen(TestData data)
        {
            Services = data.Services();
            Service = new HotkeyService(() => { });
            Hotkey = new LauncherHotkey(Service, Services);
            Hotkey.Register();
            View = new SettingsView(Services, Hotkey);
        }

        public AppServices Services { get; }

        public HotkeyService Service { get; }

        public LauncherHotkey Hotkey { get; }

        public SettingsView View { get; }

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public void A_captured_shortcut_is_saved_and_registered() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        screen.View.OnChangeHotkey(screen.View, new RoutedEventArgs());
        screen.View.HandleCapturedKey(Key.LeftCtrl, ModifierKeys.Control);
        Assert.Equal("Ctrl+…", screen.View.HotkeyCapture.Text);
        screen.View.HandleCapturedKey(Key.F21, ModifierKeys.Control | ModifierKeys.Alt);

        Assert.Equal("Ctrl+Alt+F21", data.SavedSettings.LauncherHotkey);
        Assert.Equal("Ctrl+Alt+F21", screen.View.HotkeyCapture.Text);
        Assert.StartsWith("Saved.", screen.View.HotkeyMessage.Text, StringComparison.Ordinal);
        Assert.True(screen.Hotkey.IsRegistered);
    });

    [Fact]
    public void Alt_space_asks_first_and_keeping_the_current_shortcut_changes_nothing() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        screen.View.OnChangeHotkey(screen.View, new RoutedEventArgs());
        screen.View.HandleCapturedKey(Key.Space, ModifierKeys.Alt);

        Assert.Contains("PowerToys Run", screen.View.HotkeyMessage.Text, StringComparison.Ordinal);
        Assert.Equal(Visibility.Visible, screen.View.HotkeyConfirm.Visibility);
        screen.View.OnKeepHotkey(screen.View, new RoutedEventArgs());
        Assert.Equal(TestData.HarmlessHotkey, data.SavedSettings.LauncherHotkey);
        Assert.Equal(Visibility.Collapsed, screen.View.HotkeyConfirm.Visibility);
        Assert.True(screen.Hotkey.IsRegistered);
    });

    [Fact]
    public void A_warned_shortcut_can_still_be_chosen() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        screen.View.OnChangeHotkey(screen.View, new RoutedEventArgs());
        screen.View.HandleCapturedKey(Key.F20, ModifierKeys.Windows | ModifierKeys.Shift);
        Assert.Contains("Windows keeps many Win shortcuts", screen.View.HotkeyMessage.Text, StringComparison.Ordinal);
        screen.View.OnUseHotkeyAnyway(screen.View, new RoutedEventArgs());

        Assert.Equal("Shift+Win+F20", data.SavedSettings.LauncherHotkey);
    });

    [Theory]
    [InlineData(Key.A, ModifierKeys.Shift, "Include Ctrl, Alt or Win")]
    [InlineData(Key.Tab, ModifierKeys.Alt, "switch apps")]
    [InlineData(Key.L, ModifierKeys.Windows, "locks the PC")]
    public void Rejected_shortcuts_explain_why_and_keep_the_current_one(Key key, ModifierKeys modifiers, string reason) => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        screen.View.OnChangeHotkey(screen.View, new RoutedEventArgs());
        screen.View.HandleCapturedKey(key, modifiers);

        Assert.Contains(reason, screen.View.HotkeyMessage.Text, StringComparison.Ordinal);
        Assert.Equal(TestData.HarmlessHotkey, data.SavedSettings.LauncherHotkey);
        Assert.True(screen.Hotkey.IsRegistered);
    });

    [Fact]
    public void Escape_cancels_capture_and_restores_the_shortcut() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        screen.View.OnChangeHotkey(screen.View, new RoutedEventArgs());
        Assert.False(screen.Hotkey.IsRegistered);
        screen.View.HandleCapturedKey(Key.Escape, ModifierKeys.None);

        Assert.True(screen.Hotkey.IsRegistered);
        Assert.Equal(TestData.HarmlessHotkey, screen.View.HotkeyCapture.Text);
    });

    [Fact]
    public void Engine_custom_address_and_bangs_are_saved() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);
        var view = screen.View;

        view.EngineBox.SelectedItem = view.EngineBox.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "brave");
        Assert.Equal("brave", data.SavedSettings.WebSearch.Engine);

        view.EngineBox.SelectedItem = view.EngineBox.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == SearchEngines.CustomId);
        view.CustomTemplateBox.Text = "javascript:{query}";
        view.OnUseCustomEngine(view, new RoutedEventArgs());
        Assert.Contains("Only http and https", view.CustomEngineMessage.Text, StringComparison.Ordinal);
        Assert.Equal("brave", data.SavedSettings.WebSearch.Engine);
        view.CustomTemplateBox.Text = "https://search.example.org/?s={query}";
        view.OnUseCustomEngine(view, new RoutedEventArgs());
        Assert.Equal((SearchEngines.CustomId, "https://search.example.org/?s={query}"), (data.SavedSettings.WebSearch.Engine, data.SavedSettings.WebSearch.CustomTemplate));

        view.NewBangTrigger.Text = "!mdn";
        view.NewBangName.Text = "MDN";
        view.NewBangTemplate.Text = "https://developer.mozilla.org/search?q={query}";
        view.OnAddBang(view, new RoutedEventArgs());
        view.OnRemoveBang(new Button { Tag = "a" }, new RoutedEventArgs());

        var web = data.SavedSettings.WebSearch;
        Assert.Equal("mdn", Assert.Single(web.UserBangs).Trigger);
        Assert.Equal("a", Assert.Single(web.RemovedBangs));
        var shown = ((IEnumerable<Bang>)view.BangList.ItemsSource).Select(b => b.Trigger).ToList();
        Assert.Contains("mdn", shown);
        Assert.DoesNotContain("a", shown);
        Assert.Equal("", view.NewBangTrigger.Text);

        view.OnRestoreBangs(view, new RoutedEventArgs());
        Assert.Empty(data.SavedSettings.WebSearch.UserBangs);
        Assert.Empty(data.SavedSettings.WebSearch.RemovedBangs);
    });

    [Fact]
    public void An_invalid_bang_is_not_added() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        screen.View.NewBangTrigger.Text = "x";
        screen.View.NewBangName.Text = "X";
        screen.View.NewBangTemplate.Text = "https://{query}.example.com";
        screen.View.OnAddBang(screen.View, new RoutedEventArgs());

        Assert.Contains("can't be part of the site's address", screen.View.BangMessage.Text, StringComparison.Ordinal);
        Assert.Empty(data.SavedSettings.WebSearch.UserBangs);
    });

    [Fact]
    public void Web_search_and_calculator_switches_are_saved() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);
        var view = screen.View;

        view.WebEnabledBox.IsChecked = false;
        view.WebEnabledBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        view.CalculatorBox.IsChecked = false;
        view.CalculatorBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.False(data.SavedSettings.WebSearch.Enabled);
        Assert.False(data.SavedSettings.CalculatorEnabled);
        Assert.False(view.WebOptions.IsEnabled);
    });

    [Fact]
    public void The_settings_screen_renders_off_screen() => ui.Run(() =>
    {
        using var data = new TestData();
        using var screen = new Screen(data);

        var bitmap = Render.ToBitmap(screen.View, 840, Render.NaturalHeight(screen.View, 840), "settings-launcher");

        Assert.True(Render.HasContent(bitmap));
        Assert.Null(PresentationSource.FromVisual(screen.View));
    });
}
