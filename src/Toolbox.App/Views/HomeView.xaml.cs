using System.Windows;
using System.Windows.Controls;
using Toolbox.App.Services;
using Toolbox.Core.Tools;

namespace Toolbox.App.Views;

public partial class HomeView : UserControl
{
    private readonly Action<Tool> _open;

    public HomeView(Action<Tool> open, AppServices services)
    {
        _open = open;
        InitializeComponent();
        Hint.Text = $"Pick a tool, drop in a file or a whole folder, and press Start. Press {services.Settings.LauncherHotkey} anywhere in Windows to jump straight to a tool.";
        Groups.ItemsSource = ToolRegistry.All
            .GroupBy(t => t.Category)
            .Select(g => new { Name = g.Key.DisplayName(), Tools = g.ToList() });
    }

    private void OnToolClick(object sender, RoutedEventArgs e) => _open((Tool)((Button)sender).Tag);
}
