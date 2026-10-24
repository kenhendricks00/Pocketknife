using System.Windows;

namespace Pocketknife.App.Services;

/// <summary>
/// Set POCKETKNIFE_HIDDEN=1 to run the app for automated checks without disturbing whoever uses the PC: windows
/// open off-screen without taking focus or a taskbar button, and there is no tray icon or notification.
/// </summary>
public static class HiddenMode
{
    public static bool IsOn { get; } = Environment.GetEnvironmentVariable("POCKETKNIFE_HIDDEN") == "1";

    public static void Prepare(Window window)
    {
        if (!IsOn)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        if (window.WindowStyle != WindowStyle.None)
        {
            window.WindowStyle = WindowStyle.ToolWindow;
        }
    }
}
