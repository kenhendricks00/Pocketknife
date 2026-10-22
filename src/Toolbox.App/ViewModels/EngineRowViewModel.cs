using Microsoft.Win32;
using Toolbox.App.Services;
using Toolbox.Core.Engines;
using Toolbox.Core.Running;

namespace Toolbox.App.ViewModels;

public sealed class EngineRowViewModel : Observable
{
    private readonly AppServices _services;
    private string _status = "";
    private bool _busy;

    public EngineRowViewModel(EngineSpec engine, AppServices services)
    {
        Engine = engine;
        _services = services;
        DownloadCommand = new RelayCommand(() => _ = DownloadAsync(), () => !_busy && State == EngineState.NotInstalled);
        ChooseCommand = new RelayCommand(Choose, () => !_busy);
        ResetCommand = new RelayCommand(() => Change(() => _services.SetEnginePath(Engine, null)), () => State is EngineState.Custom or EngineState.CustomMissing);
        RemoveCommand = new RelayCommand(() => Change(() => _services.Engines.Remove(Engine)), () => !_busy && State == EngineState.Installed);
        Refresh();
    }

    public EngineSpec Engine { get; }

    public string Title => $"{Engine.Name} · {Engine.Version}";

    public string Details => $"{Engine.SizeText} download · {Engine.License}";

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public RelayCommand DownloadCommand { get; }

    public RelayCommand ChooseCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand RemoveCommand { get; }

    private EngineState State => _services.Engines.GetStatus(Engine).State;

    private void Refresh()
    {
        var status = _services.Engines.GetStatus(Engine);
        Status = status.State switch
        {
            EngineState.Installed => "Installed and verified",
            EngineState.Custom => $"Using your copy: {status.Path}",
            EngineState.CustomMissing => $"Your copy is missing: {status.Path}",
            _ => "Not installed. Downloads the first time a tool needs it.",
        };
    }

    private void Change(Action action)
    {
        action();
        Refresh();
    }

    private void Choose()
    {
        var extension = Path.GetExtension(Engine.MainFile);
        var dialog = new OpenFileDialog
        {
            Title = $"Choose your {Engine.MainFile}",
            Filter = $"{Engine.MainFile}|{Engine.MainFile}|{extension} files|*{extension}",
        };
        if (dialog.ShowDialog() == true)
        {
            Change(() => _services.SetEnginePath(Engine, dialog.FileName));
        }
    }

    private async Task DownloadAsync()
    {
        _busy = true;
        try
        {
            var progress = new Progress<double>(f => Status = $"Downloading… {f:P0}");
            await Task.Run(() => _services.Engines.EnsureAsync(Engine, progress));
            Refresh();
        }
        catch (Exception ex) when (ex is ToolException or HttpRequestException or IOException or TaskCanceledException)
        {
            Status = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
