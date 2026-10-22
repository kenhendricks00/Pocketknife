using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Toolbox.App.ViewModels;

namespace Toolbox.App.Views;

public partial class ToolView : UserControl
{
    private readonly ToolViewModel _model;
    private readonly Action _openSettings;

    public ToolView(ToolViewModel model, Action openSettings)
    {
        _model = model;
        _openSettings = openSettings;
        DataContext = model;
        InitializeComponent();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolViewModel.IsRunning) && !model.IsRunning)
            {
                Dispatcher.BeginInvoke(() => (model.Failures.Count > 0 ? FailureCard : ResultCard).BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
    }

    private void OnChooseFiles(object sender, RoutedEventArgs e)
    {
        var input = _model.FileInput!;
        var patterns = string.Join(";", input.Extensions.Select(x => "*" + x));
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = $"Supported {input.Noun}|{patterns}|All files|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _model.AddInputs(dialog.FileNames);
        }
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to process" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _model.AddInputs([dialog.FolderName]);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentFillColorDefaultBrush");
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropZone.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

    private void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _model.AddInputs(paths);
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _openSettings();
}
