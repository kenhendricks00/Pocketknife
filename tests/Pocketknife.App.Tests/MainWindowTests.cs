using System.Windows.Interop;
using Pocketknife.App.Services;
using Pocketknife.App.Views;

namespace Pocketknife.App.Tests;

public class MainWindowTests(UiThread ui)
{
    [Fact]
    public void Creating_the_main_window_neither_shows_it_nor_opens_a_tool() => ui.Run(() =>
    {
        using var data = new TestData();
        var services = data.Services();
        using var hotkeys = new HotkeyService(() => { });

        var window = new MainWindow(services, new LauncherHotkey(hotkeys, services));

        Assert.False(window.IsVisible);
        Assert.Equal(IntPtr.Zero, new WindowInteropHelper(window).Handle);
        Assert.IsType<HomeView>(window.Page.Content);
        Assert.Null(window.ToolList.SelectedItem);
        window.Close();
    });
}
