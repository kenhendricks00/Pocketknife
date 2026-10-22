using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toolbox.Core.Tools;

namespace Toolbox.App;

/// <summary>The system-wide quick launcher: type a few letters and press Enter to open a tool.</summary>
public partial class LauncherWindow : Window
{
    private readonly Action<string> _open;

    public LauncherWindow(Action<string> open)
    {
        _open = open;
        InitializeComponent();
        UpdateResults();
    }

    public void Summon()
    {
        Query.Text = "";
        UpdateResults();
        Show();
        Activate();
        Query.Focus();
    }

    private void UpdateResults()
    {
        Results.ItemsSource = ToolSearch.Rank(ToolRegistry.All, Query.Text);
        Results.SelectedIndex = Results.Items.Count > 0 ? 0 : -1;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e) => UpdateResults();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                break;
            case Key.Enter:
                OpenSelected();
                break;
            case Key.Down when Results.Items.Count > 0:
                Results.SelectedIndex = (Results.SelectedIndex + 1) % Results.Items.Count;
                Results.ScrollIntoView(Results.SelectedItem);
                break;
            case Key.Up when Results.Items.Count > 0:
                Results.SelectedIndex = (Results.SelectedIndex - 1 + Results.Items.Count) % Results.Items.Count;
                Results.ScrollIntoView(Results.SelectedItem);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnResultChosen(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (Results.SelectedItem is Tool tool)
        {
            Hide();
            _open(tool.Id);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();
}
