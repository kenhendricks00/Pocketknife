using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pocketknife.App.Services;
using Pocketknife.Core.Settings;

[assembly: AssemblyFixture(typeof(Pocketknife.App.Tests.UiThread))]

namespace Pocketknife.App.Tests;

/// <summary>
/// One STA thread with the app's styles and theme, shared by every test. Views are created and
/// laid out on it but never shown, so nothing appears on screen, takes focus or reaches the taskbar.
/// </summary>
public sealed class UiThread : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public UiThread()
    {
        // Safety net: even if a test makes a window show itself, hidden mode keeps it off-screen, unactivated and
        // out of the taskbar. Set before any app type reads it.
        Environment.SetEnvironmentVariable("POCKETKNIFE_HIDDEN", "1");
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            // A plain Application, never the real App: App.OnStartup would take the single-instance lock,
            // read the user's settings, add a tray icon and register the hotkey.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppResources.Apply(app, ThemeMode.Light);
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "UI tests",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        _dispatcher = dispatcher!;
    }

    public T Run<T>(Func<T> action) => _dispatcher.Invoke(action);

    public void Run(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(10));
    }
}

/// <summary>A throwaway data folder holding a settings file, so tests never touch the real one.</summary>
public sealed class TestData : IDisposable
{
    /// <summary>A shortcut nobody presses (there is no F24 key on normal keyboards), so registering it can't steal keystrokes.</summary>
    public const string HarmlessHotkey = "Ctrl+Alt+Shift+F24";

    public TestData(AppSettings? settings = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "pocketknife-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        new SettingsStore(Path.Combine(Root, "settings.json")).Save(settings ?? new AppSettings { LauncherHotkey = HarmlessHotkey });
    }

    public string Root { get; }

    public AppSettings SavedSettings => new SettingsStore(Path.Combine(Root, "settings.json")).Load();

    public AppServices Services() => new(Root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class Render
{
    /// <summary>
    /// Lays out <paramref name="element"/> off-screen and renders it with RenderTargetBitmap. When the
    /// POCKETKNIFE_SCREENSHOTS variable names a folder, the PNG is also saved there as <paramref name="name"/>.
    /// </summary>
    public static BitmapSource ToBitmap(FrameworkElement element, double width, double height, string name)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            context.DrawRectangle((Brush?)element.TryFindResource("ApplicationBackgroundBrush") ?? Brushes.White, null, new Rect(0, 0, width, height));
        }

        bitmap.Render(background);
        bitmap.Render(element);
        bitmap.Freeze();
        if (Environment.GetEnvironmentVariable("POCKETKNIFE_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        }

        return bitmap;
    }

    /// <summary>True when the bitmap has more than one color, i.e. something was actually drawn.</summary>
    public static bool HasContent(BitmapSource bitmap)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels.Distinct().Take(2).Count() > 1;
    }

    /// <summary>The height <paramref name="element"/> wants at <paramref name="width"/>.</summary>
    public static double NaturalHeight(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        return Math.Ceiling(element.DesiredSize.Height);
    }
}
