using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pocketknife.App.Services;
using Pocketknife.App.Views;

namespace Pocketknife.App.Tests;

/// <summary>
/// Renders the main window's sidebar off-screen in light and dark and measures every nav row: labels must meet
/// WCAG AA contrast (4.5:1), icons the 3:1 for graphics, and "All tools" and "Settings" must be laid out exactly
/// like the tool rows above them.
/// </summary>
public class SidebarTests(UiThread ui)
{
    private sealed record Row(string Label, double Contrast, double IconContrast, double IconSize, double IconLeft, double LabelLeft, double CenterOffset, double Height);

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Nav_rows_are_readable_and_alike(string theme) => ui.Run(() =>
    {
        var previous = Application.Current.ThemeMode;
        Application.Current.ThemeMode = new ThemeMode(theme);
        try
        {
            using var data = new TestData();
            var window = NewWindow(data);
            var sidebar = (FrameworkElement)((Grid)window.Content).Children[0];
            // Tall enough that no tool row scrolls out of view.
            var bitmap = Render.ToBitmap(sidebar, 280, 1000, $"sidebar-{theme.ToLowerInvariant()}");
            var rows = Rows(sidebar, bitmap);
            window.Close();

            var footer = rows.Where(r => r.Label is "All tools" or "Settings").ToList();
            var tools = rows.Except(footer).ToList();
            Assert.Equal(2, footer.Count);
            Assert.NotEmpty(tools);
            var report = string.Join("\n", rows.Select(r =>
                $"{r.Label}: text {r.Contrast:0.0}:1, icon {r.IconContrast:0.0}:1, icon {r.IconSize} px at {r.IconLeft}, label at {r.LabelLeft}, off-center {r.CenterOffset:0.0} px, row {r.Height} px"));
            foreach (var row in rows)
            {
                Assert.True(row.Contrast >= 4.5, $"{theme}: '{row.Label}' text contrast {row.Contrast:0.00}:1 is below 4.5:1.\n{report}");
                Assert.True(row.IconContrast >= 3, $"{theme}: '{row.Label}' icon contrast {row.IconContrast:0.00}:1 is below 3:1.\n{report}");
                Assert.True(row.CenterOffset <= 0.5, $"{theme}: '{row.Label}' icon and label centers are {row.CenterOffset:0.0} px apart.\n{report}");
            }

            var tool = tools[0];
            Assert.All(footer, f => Assert.Equal(
                (tool.IconSize, tool.IconLeft, tool.LabelLeft, tool.Height),
                (f.IconSize, f.IconLeft, f.LabelLeft, f.Height)));
        }
        finally
        {
            Application.Current.ThemeMode = previous;
        }
    });

    [Fact]
    public void All_tools_and_settings_are_selectable_rows_that_follow_the_open_page() => ui.Run(() =>
    {
        using var data = new TestData();
        var window = NewWindow(data);
        Assert.Equal(0, window.PageList.SelectedIndex);
        Assert.IsType<HomeView>(window.Page.Content);

        window.PageList.SelectedIndex = 1;
        Assert.IsType<SettingsView>(window.Page.Content);
        Assert.Null(window.ToolList.SelectedItem);

        window.PageList.SelectedIndex = 0;
        Assert.IsType<HomeView>(window.Page.Content);
        Assert.Equal(0, window.PageList.SelectedIndex);
        Assert.False(window.IsVisible);
        window.Close();
    });

    private static MainWindow NewWindow(TestData data)
    {
        var services = data.Services();
        var hotkeys = new HotkeyService(() => { });
        var window = new MainWindow(services, new LauncherHotkey(hotkeys, services));
        window.Closed += (_, _) => hotkeys.Dispose();
        return window;
    }

    /// <summary>Each nav row: the icon and label text blocks inside a sidebar ListBoxItem, measured in sidebar pixels.</summary>
    private static List<Row> Rows(FrameworkElement sidebar, BitmapSource bitmap)
    {
        var rows = new List<Row>();
        foreach (var item in Descendants(sidebar).OfType<ListBoxItem>())
        {
            var texts = Descendants(item).OfType<TextBlock>().Where(t => t.ActualWidth > 0 && !string.IsNullOrEmpty(t.Text)).ToList();
            var icon = texts.FirstOrDefault(t => t.FontFamily.Source.Contains("Fluent", StringComparison.Ordinal));
            var label = texts.FirstOrDefault(t => t != icon);
            if (icon is null || label is null)
            {
                continue;
            }

            var labelBox = Bounds(label, sidebar);
            var iconBox = Bounds(icon, sidebar);
            rows.Add(new Row(
                label.Text,
                Contrast(bitmap, labelBox),
                Contrast(bitmap, iconBox),
                icon.FontSize,
                Math.Round(iconBox.X),
                Math.Round(labelBox.X),
                Math.Abs((iconBox.Top + iconBox.Bottom - labelBox.Top - labelBox.Bottom) / 2),
                Math.Round(item.ActualHeight)));
        }

        return rows;
    }

    private static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>WCAG contrast between the most common color in the box (the background) and the strongest ink in it.</summary>
    private static double Contrast(BitmapSource bitmap, Rect box)
    {
        var x = Math.Max(0, (int)box.X);
        var y = Math.Max(0, (int)box.Y);
        var width = Math.Min(bitmap.PixelWidth - x, (int)Math.Ceiling(box.Width));
        var height = Math.Min(bitmap.PixelHeight - y, (int)Math.Ceiling(box.Height));
        var pixels = new uint[width * height];
        bitmap.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        var background = Luminance(pixels.GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key);
        return pixels.Max(p => Ratio(Luminance(p), background));
    }

    private static double Luminance(uint bgra)
    {
        static double Channel(uint value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel((bgra >> 16) & 0xFF)) + (0.7152 * Channel((bgra >> 8) & 0xFF)) + (0.0722 * Channel(bgra & 0xFF));
    }

    private static double Ratio(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
}
