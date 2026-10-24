using System.Windows;
using Pocketknife.Core.Launcher;
using Pocketknife.Core.Settings;

namespace Pocketknife.App.Tests;

/// <summary>Drives the real launcher window without showing it; opening, copying and browsing are captured.</summary>
public class LauncherWindowTests(UiThread ui)
{
    private sealed record Launcher(LauncherWindow Window, List<string> Tools, List<string> Copied, List<Uri> Browsed);

    private static Launcher Create(TestData data)
    {
        var tools = new List<string>();
        var copied = new List<string>();
        var browsed = new List<Uri>();
        var window = new LauncherWindow(data.Services(), tools.Add, copied.Add, browsed.Add);
        return new Launcher(window, tools, copied, browsed);
    }

    private static List<string> Type(Launcher launcher, string text)
    {
        launcher.Window.Query.Text = text;
        return [.. launcher.Window.Rows.Select(r => r.Name)];
    }

    [Fact]
    public void A_calculation_is_the_top_row_and_enter_copies_it() => ui.Run(() =>
    {
        using var data = new TestData();
        var launcher = Create(data);

        var rows = Type(launcher, "12*(3+4)");
        launcher.Window.OpenSelected();

        Assert.Equal("= 84", rows[0]);
        Assert.Equal("84", Assert.Single(launcher.Copied));
        Assert.Empty(launcher.Browsed);
        Assert.False(launcher.Window.IsVisible);
        launcher.Window.Close();
    });

    [Fact]
    public void A_bang_previews_its_site_and_enter_opens_the_encoded_search() => ui.Run(() =>
    {
        using var data = new TestData();
        var launcher = Create(data);

        var rows = Type(launcher, "!yt lofi & chill");
        launcher.Window.OpenSelected();

        Assert.Equal("Search YouTube for 'lofi & chill'", Assert.Single(rows));
        Assert.Equal("https://www.youtube.com/results?search_query=lofi%20%26%20chill", Assert.Single(launcher.Browsed).AbsoluteUri);
        launcher.Window.Close();
    });

    [Fact]
    public void A_bare_bang_opens_the_site() => ui.Run(() =>
    {
        using var data = new TestData();
        var launcher = Create(data);

        Assert.Equal("Open GitHub", Assert.Single(Type(launcher, "!gh")));
        launcher.Window.OpenSelected();
        Assert.Equal("https://github.com/", Assert.Single(launcher.Browsed).AbsoluteUri);
        launcher.Window.Close();
    });

    [Fact]
    public void Tools_come_before_the_web_search_and_enter_opens_the_tool() => ui.Run(() =>
    {
        using var data = new TestData();
        var launcher = Create(data);

        var rows = Type(launcher, "gif");
        launcher.Window.OpenSelected();

        Assert.Equal("Video to GIF", rows[0]);
        Assert.Equal("Search DuckDuckGo for 'gif'", rows[^1]);
        Assert.Equal("video-to-gif", Assert.Single(launcher.Tools));
        launcher.Window.Close();
    });

    [Fact]
    public void Choosing_the_web_row_with_the_arrow_keys_searches_the_chosen_engine() => ui.Run(() =>
    {
        using var data = new TestData(new AppSettings { LauncherHotkey = TestData.HarmlessHotkey, WebSearch = new WebSearchSettings { Engine = "google" } });
        var launcher = Create(data);

        Type(launcher, "gif");
        launcher.Window.Results.SelectedIndex = launcher.Window.Results.Items.Count - 1;
        launcher.Window.OpenSelected();

        Assert.Equal("https://www.google.com/search?q=gif", Assert.Single(launcher.Browsed).AbsoluteUri);
        launcher.Window.Close();
    });

    [Fact]
    public void Turning_web_search_and_the_calculator_off_removes_their_rows() => ui.Run(() =>
    {
        using var data = new TestData(new AppSettings
        {
            LauncherHotkey = TestData.HarmlessHotkey,
            CalculatorEnabled = false,
            WebSearch = new WebSearchSettings { Enabled = false },
        });
        var launcher = Create(data);

        Assert.Empty(Type(launcher, "2+2"));
        Assert.Empty(Type(launcher, "!yt cats"));
        launcher.Window.OpenSelected();
        Assert.Empty(launcher.Copied.Concat(launcher.Browsed.Select(u => u.ToString())));
        launcher.Window.Close();
    });

    [Fact]
    public void The_launcher_renders_off_screen() => ui.Run(() =>
    {
        using var data = new TestData();
        var launcher = Create(data);

        foreach (var (text, name) in new[] { ("12*(3+4)", "launcher-calculator"), ("!yt lofi beats", "launcher-bang"), ("gif", "launcher") })
        {
            Type(launcher, text);
            var content = (FrameworkElement)launcher.Window.Content;
            Assert.True(Render.HasContent(Render.ToBitmap(content, 600, Render.NaturalHeight(content, 600), name)));
        }

        Assert.False(launcher.Window.IsVisible);
        launcher.Window.Close();
    });
}
