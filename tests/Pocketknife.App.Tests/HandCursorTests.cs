using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Pocketknife.App.Services;
using Pocketknife.App.ViewModels;
using Pocketknife.App.Views;
using Pocketknife.Core.Tools;

namespace Pocketknife.App.Tests;

/// <summary>
/// Walks the real screens (laid out off-screen, never shown) and checks the cursor WPF would use over each control:
/// the nearest element, itself or an ancestor, that sets one.
/// </summary>
public class HandCursorTests(UiThread ui)
{
    private static Cursor? EffectiveCursor(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Cursor: { } cursor })
            {
                return cursor;
            }
        }

        return null;
    }

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

    // Scroll bars and the clear button inside a text box belong to those controls, which keep their own cursors.
    private static bool InsideScrollBarOrTextBox(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollBar or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Lays out <paramref name="root"/> and checks every clickable control in it. Returns how many were checked.</summary>
    private static int AssertCursors(FrameworkElement root, double width)
    {
        Render.ToBitmap(root, width, Math.Min(Render.NaturalHeight(root, width), 4000), "cursor-check");
        var clickable = Descendants(root)
            .OfType<Control>()
            .Where(c => c is ButtonBase or ComboBox or ListBoxItem or ComboBoxItem && !InsideScrollBarOrTextBox(c))
            .ToList();
        foreach (var control in clickable)
        {
            var cursor = EffectiveCursor(control);
            var label = $"{control.GetType().Name} '{AutomationName(control)}' (enabled: {control.IsEnabled})";
            if (control.IsEnabled)
            {
                Assert.True(cursor == Cursors.Hand, $"{label} shows {cursor?.ToString() ?? "the default cursor"} instead of the hand.");
            }
            else
            {
                Assert.True(cursor != Cursors.Hand, $"{label} is disabled but still shows the hand.");
            }
        }

        foreach (var box in Descendants(root).OfType<TextBox>())
        {
            Assert.True(EffectiveCursor(box) != Cursors.Hand, $"Text box '{AutomationName(box)}' shows the hand.");
        }

        return clickable.Count;
    }

    private static string AutomationName(Control control) =>
        System.Windows.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : control.ToString();

    [Fact]
    public void Home_tool_cards_and_the_sidebar_use_the_hand() => ui.Run(() =>
    {
        using var data = new TestData();
        var services = data.Services();
        using var hotkeys = new HotkeyService(() => { });
        var window = new MainWindow(services, new LauncherHotkey(hotkeys, services));

        Assert.True(AssertCursors((FrameworkElement)window.Content, 1100) >= ToolRegistry.All.Count * 2);
        Assert.False(window.IsVisible);
        window.Close();
    });

    [Fact]
    public void Tool_screens_use_the_hand_on_buttons_and_choices() => ui.Run(() =>
    {
        using var data = new TestData();
        var services = data.Services();
        foreach (var tool in ToolRegistry.All)
        {
            var view = new ToolView(new ToolViewModel(tool, services), () => { });
            if (view.DataContext is ToolViewModel { FileInput: not null } model)
            {
                model.AddInputs([@"C:\Samples\example" + model.FileInput.Extensions[0]]);
            }

            Assert.True(AssertCursors(view, 820) > 0, tool.Id);
        }
    });

    [Fact]
    public void Settings_rows_use_the_hand_and_disabled_ones_keep_the_arrow() => ui.Run(() =>
    {
        using var data = new TestData();
        var services = data.Services();
        using var hotkeys = new HotkeyService(() => { });
        var view = new SettingsView(services, new LauncherHotkey(hotkeys, services));

        AssertCursors(view, 840);

        view.WebEnabledBox.IsChecked = false;
        view.WebEnabledBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(view.EngineBox.IsEnabled);
        AssertCursors(view, 840);
        Assert.True(EffectiveCursor(view.EngineBox) != Cursors.Hand);
        Assert.True(EffectiveCursor(view.HotkeyCapture) != Cursors.Hand);
    });

    [Fact]
    public void Launcher_results_use_the_hand_and_the_search_box_does_not() => ui.Run(() =>
    {
        using var data = new TestData();
        var launcher = new LauncherWindow(data.Services(), _ => { }, _ => { }, _ => { });
        launcher.Query.Text = "gif";

        Assert.True(AssertCursors((FrameworkElement)launcher.Content, 600) >= 2);
        Assert.True(EffectiveCursor(launcher.Query) != Cursors.Hand);
        Assert.All(Descendants(launcher.Query).OfType<ButtonBase>(), b => Assert.Equal(Cursors.Arrow, EffectiveCursor(b)));
        launcher.Close();
    });

    [Fact]
    public void Clickable_styles_keep_the_fluent_look_and_tests_never_start_the_real_app() => ui.Run(() =>
    {
        var button = (Style)Application.Current.FindResource(typeof(Button));

        Assert.IsNotType<App>(Application.Current);
        var fluent = new List<Style>();
        for (var style = button.BasedOn; style is not null; style = style.BasedOn)
        {
            fluent.Add(style);
        }

        Assert.Contains(fluent, style => style.Setters.OfType<Setter>().Any(s => s.Property == Control.TemplateProperty));
        Assert.Same(Application.Current.FindResource(typeof(ListBoxItem)), ((Style)Application.Current.FindResource("NavItem")).BasedOn);
    });
}
