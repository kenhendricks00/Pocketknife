using System.Windows;

namespace Pocketknife.App;

/// <summary>
/// The app's look: the Fluent theme plus Styles.xaml. Kept apart from <see cref="App"/> so tests can style their
/// own plain <see cref="Application"/> without starting the real app (single-instance lock, tray icon, hotkey).
/// </summary>
public static class AppResources
{
    private static readonly Uri s_styles = new("pack://application:,,,/Pocketknife;component/Styles.xaml");

    public static void Apply(Application application, ThemeMode theme)
    {
        // The theme goes first: the styles in Styles.xaml are based on the Fluent control styles.
        application.ThemeMode = theme;
        if (!application.Resources.MergedDictionaries.Any(d => d.Source == s_styles))
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = s_styles });
        }
    }
}
