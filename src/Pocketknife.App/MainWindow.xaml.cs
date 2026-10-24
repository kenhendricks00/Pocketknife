using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Pocketknife.App.Services;
using Pocketknife.App.ViewModels;
using Pocketknife.App.Views;
using Pocketknife.Core.Tools;

namespace Pocketknife.App;

public partial class MainWindow : Window
{
    private static readonly NavLink s_allTools = new("All tools", "\uE80F");
    private static readonly NavLink s_settings = new("Settings", "\uE713");
    private readonly AppServices _services;
    private readonly LauncherHotkey _hotkey;
    private readonly Dictionary<string, ToolView> _toolViews = [];
    private readonly ListCollectionView _tools;
    private bool _navigating;

    public MainWindow(AppServices services, LauncherHotkey hotkey)
    {
        _services = services;
        _hotkey = hotkey;
        InitializeComponent();
        HiddenMode.Prepare(this);
        _tools = new ListCollectionView(ToolRegistry.All.ToList());
        _tools.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Tool.Category), new CategoryNameConverter()));
        ToolList.ItemsSource = _tools;
        PageList.ItemsSource = new[] { s_allTools, s_settings };
        FocusSearchCommand = new RelayCommand(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });
        ShowHome();
    }

    public ICommand FocusSearchCommand { get; }


    public void Summon()
    {
        Show();
        if (HiddenMode.IsOn)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void OpenTool(string id, IReadOnlyList<string> inputs)
    {
        var tool = ToolRegistry.Find(id);
        if (tool is null)
        {
            ShowHome();
            return;
        }

        if (!_toolViews.TryGetValue(tool.Id, out var view))
        {
            view = new ToolView(new ToolViewModel(tool, _services), OpenSettings);
            _toolViews[tool.Id] = view;
        }

        var model = (ToolViewModel)view.DataContext;
        model.RefreshEngineNotes();
        model.AddInputs(inputs.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath));
        Page.Content = view;
        _navigating = true;
        SearchBox.Text = "";
        _navigating = false;
        Select(tool);
        ToolList.ScrollIntoView(tool);
        Summon();
    }

    private void ShowHome()
    {
        Page.Content = new HomeView(tool => OpenTool(tool.Id, []), _services);
        Select(s_allTools);
    }

    private void OpenSettings()
    {
        Page.Content = new SettingsView(_services, _hotkey);
        Select(s_settings);
    }

    /// <summary>Marks the open page in the sidebar: a tool in the tool list, or All tools or Settings below it.</summary>
    private void Select(object page)
    {
        _navigating = true;
        ToolList.SelectedItem = page as Tool;
        PageList.SelectedItem = page as NavLink;
        _navigating = false;
    }

    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_navigating)
        {
            return;
        }

        if (Equals(PageList.SelectedItem, s_allTools))
        {
            ShowHome();
        }
        else if (Equals(PageList.SelectedItem, s_settings))
        {
            OpenSettings();
        }
    }

    private void OnToolSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_navigating && ToolList.SelectedItem is Tool tool)
        {
            OpenTool(tool.Id, []);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var matches = ToolSearch.Rank(ToolRegistry.All, SearchBox.Text).ToHashSet();
        _tools.Filter = o => matches.Contains((Tool)o);
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ToolSearch.Rank(ToolRegistry.All, SearchBox.Text) is [var best, ..])
        {
            OpenTool(best.Id, []);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ToolList.Items.Count > 0)
        {
            ToolList.Focus();
            e.Handled = true;
        }
    }

    private sealed record NavLink(string Name, string Glyph);

    private sealed class CategoryNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            ((ToolCategory)value).DisplayName();

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
