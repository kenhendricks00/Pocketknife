using Forms = System.Windows.Forms;

namespace Pocketknife.App.Services;

/// <summary>The notification-area icon that keeps the launcher hotkey available while the window is closed.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private bool _toldAboutTray;

    public TrayIcon(Action open, Action launcher, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Pocketknife", null, (_, _) => open());
        menu.Items.Add("Find a tool…", null, (_, _) => launcher());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Pocketknife;component/Assets/app.ico")).Stream)
        {
            _image = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "Pocketknife",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };
    }

    public void Warn(string title, string text) => _icon.ShowBalloonTip(8000, title, text, Forms.ToolTipIcon.Warning);

    public void TellStillRunning(string hotkey)
    {
        if (!_toldAboutTray)
        {
            _toldAboutTray = true;
            _icon.ShowBalloonTip(4000, "Pocketknife is still running", $"Press {hotkey} to find a tool, or use this icon. Quit from its menu.", Forms.ToolTipIcon.None);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
