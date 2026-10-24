using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pocketknife.App.Services;
using Pocketknife.Core.Launcher;
using Pocketknife.Core.Tools;

namespace Pocketknife.App;

/// <summary>One row in the launcher. Glyph, Name and Summary are what the row template shows.</summary>
public sealed record LauncherRow(LauncherResult Result, string Glyph, string Name, string Summary)
{
    public static LauncherRow From(LauncherResult result) => result switch
    {
        ToolResult { Tool: var tool } => new(result, tool.Glyph, tool.Name, tool.Summary),
        CalculationResult { Calculation: var calc } => new(result, "\uE8EF", $"= {calc.Display}", "Calculator · press Enter to copy the result"),
        WebResult { Target: var web } => new(
            result,
            "\uE774",
            web.Query.Length == 0 ? $"Open {web.SiteName}" : $"Search {web.SiteName} for '{web.Query}'",
            $"{web.Url.Host} · opens in your browser"),
        _ => throw new NotSupportedException(result.GetType().Name),
    };
}

/// <summary>The system-wide quick launcher: type to find a tool, calculate, or search the web.</summary>
public partial class LauncherWindow : Window
{
    private readonly AppServices _services;
    private readonly Action<string> _openTool;
    private readonly Action<string> _copy;
    private readonly Action<Uri> _openWeb;

    /// <param name="copy">Puts text on the clipboard; tests pass their own so the real clipboard is untouched.</param>
    /// <param name="openWeb">Opens a web address in the browser; tests pass their own.</param>
    public LauncherWindow(AppServices services, Action<string> openTool, Action<string>? copy = null, Action<Uri>? openWeb = null)
    {
        _services = services;
        _openTool = openTool;
        _copy = copy ?? CopyToClipboard;
        _openWeb = openWeb ?? OpenInBrowser;
        InitializeComponent();
        HiddenMode.Prepare(this);
        UpdateResults();
    }

    public void Summon()
    {
        Query.Text = "";
        UpdateResults();
        Show();
        if (!HiddenMode.IsOn)
        {
            Activate();
            Query.Focus();
        }
    }

    internal IReadOnlyList<LauncherRow> Rows => (IReadOnlyList<LauncherRow>)Results.ItemsSource;

    private void UpdateResults()
    {
        Results.ItemsSource = LauncherSearch.Find(Query.Text, ToolRegistry.All, _services.LauncherOptions).Select(LauncherRow.From).ToList();
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

    internal void OpenSelected()
    {
        if (Results.SelectedItem is not LauncherRow row)
        {
            return;
        }

        Hide();
        switch (row.Result)
        {
            case ToolResult { Tool: var tool }:
                _openTool(tool.Id);
                break;
            case CalculationResult { Calculation: var calc }:
                _copy(calc.CopyText);
                break;
            case WebResult { Target: var web } when UrlTemplate.IsWeb(web.Url):
                _openWeb(web.Url);
                break;
        }
    }

    private static void OpenInBrowser(Uri url) =>
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();

    private static void CopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();
}
