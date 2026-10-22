using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Toolbox.App.Services;
using Toolbox.Core.Engines;
using Toolbox.Core.Running;
using Toolbox.Core.Tools;

namespace Toolbox.App.ViewModels;

public sealed record EngineNote(string Text);

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "The token source lives only for one run and is disposed when the run ends.")]
public sealed class ToolViewModel : Observable
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cancellation;
    private string _text = "";
    private bool _isRunning;
    private double _progress;
    private string _status = "";
    private string? _outputDirectory;
    private string _resultSummary = "";
    private string _messages = "";

    public ToolViewModel(Tool tool, AppServices services)
    {
        Tool = tool;
        _services = services;
        Options = [.. tool.Options.Select(OptionViewModel.Create)];
        foreach (var option in Options)
        {
            option.PropertyChanged += OnOptionChanged;
        }

        RunCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsRunning);
        OpenOutputCommand = new RelayCommand(() => OpenFolder(OutputDirectory), () => OutputDirectory is not null);
        RemoveInputCommand = new RelayCommand(p => Inputs.Remove((string)p!));
        MoveUpCommand = new RelayCommand(p => Move((string)p!, -1));
        MoveDownCommand = new RelayCommand(p => Move((string)p!, 1));
        ClearInputsCommand = new RelayCommand(Inputs.Clear, () => Inputs.Count > 0 && !IsRunning);
        services.SettingsChanged += (_, _) => OnPropertyChanged(nameof(OutputHint));
        RefreshVisibility();
        RefreshEngineNotes();
    }

    public Tool Tool { get; }

    public FileInput? FileInput => Tool.Input as FileInput;

    public TextInput? TextInput => Tool.Input as TextInput;

    public bool CanReorder => FileInput?.Combine == true;

    public string AcceptedTypes => FileInput is null ? "" : string.Join("  ", FileInput.Extensions);

    public IReadOnlyList<OptionViewModel> Options { get; }

    public bool HasOptions => Options.Count > 0;

    public ObservableCollection<string> Inputs { get; } = [];

    public ObservableCollection<EngineNote> EngineNotes { get; } = [];

    public ObservableCollection<string> Outputs { get; } = [];

    public ObservableCollection<RunFailure> Failures { get; } = [];

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string? OutputDirectory
    {
        get => _outputDirectory;
        private set => Set(ref _outputDirectory, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => Set(ref _resultSummary, value);
    }

    public string Messages
    {
        get => _messages;
        private set => Set(ref _messages, value);
    }

    public string OutputHint => $"Results go to a new dated folder in {_services.Settings.EffectiveOutputRoot}. Your files are never changed.";

    public RelayCommand RunCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand OpenOutputCommand { get; }

    public RelayCommand RemoveInputCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public RelayCommand ClearInputsCommand { get; }

    public void AddInputs(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Inputs.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Inputs.Add(path);
            }
        }
    }

    public void RefreshEngineNotes()
    {
        EngineNotes.Clear();
        foreach (var engine in Tool.RequiredEngines(CurrentValues()))
        {
            var status = _services.Engines.GetStatus(engine);
            if (status.State == EngineState.NotInstalled)
            {
                EngineNotes.Add(new($"Needs {engine.Name} ({engine.SizeText}). It downloads once, the first time you run this tool, and is checked against a known SHA-256."));
            }
            else if (status.State == EngineState.CustomMissing)
            {
                EngineNotes.Add(new($"Your {engine.Name} copy is missing ({status.Path}). Fix it in Settings."));
            }
        }
    }

    private OptionValues CurrentValues()
    {
        try
        {
            return new OptionValues(Tool.Options, Options.ToDictionary(o => o.Option.Key, o => (object?)o.Value));
        }
        catch (ArgumentException)
        {
            return new OptionValues(Tool.Options);
        }
    }

    private async Task RunAsync()
    {
        OptionValues values;
        try
        {
            values = new OptionValues(Tool.Options, Options.Where(o => o.IsVisible).ToDictionary(o => o.Option.Key, o => (object?)o.Value));
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsRunning = true;
        Outputs.Clear();
        Failures.Clear();
        Messages = "";
        ResultSummary = "";
        OutputDirectory = null;
        Progress = 0;
        Status = "Starting…";
        var progress = new Progress<RunProgress>(p =>
        {
            Status = p.Status;
            Progress = p.Fraction ?? Progress;
        });

        try
        {
            var options = Tool.Options.ToDictionary(o => o.Key, o => (object?)values[o.Key]);
            var request = new RunRequest([.. Inputs], Text, options);
            var result = await Task.Run(() => _services.Runner.RunAsync(Tool, request, progress, l => Trace.WriteLine(l), _cancellation.Token));
            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled. Anything already finished was kept.";
        }
        catch (ToolException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
            RefreshEngineNotes();
        }
    }

    private void ShowResult(RunResult result)
    {
        OutputDirectory = result.OutputDirectory;
        foreach (var output in result.Outputs)
        {
            Outputs.Add(Path.GetFileName(output));
        }

        foreach (var failure in result.Failures)
        {
            Failures.Add(failure);
        }

        Messages = string.Join(Environment.NewLine, result.Messages);
        Progress = 1;
        var saved = result.Outputs.Count == 1 ? "Saved 1 file" : $"Saved {result.Outputs.Count} files";
        Status = result.Failures.Count == 0 ? "Done" : $"Finished with {result.Failures.Count} {(result.Failures.Count == 1 ? "problem" : "problems")}";
        ResultSummary = result.Outputs.Count > 0 ? $"{saved} to {result.OutputDirectory}" : "Nothing was saved.";
    }

    private void ShowError(string message)
    {
        Status = message;
        Progress = 0;
    }

    private void Move(string item, int delta)
    {
        var index = Inputs.IndexOf(item);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < Inputs.Count)
        {
            Inputs.Move(index, target);
        }
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OptionViewModel.IsVisible))
        {
            RefreshVisibility();
            RefreshEngineNotes();
        }
    }

    private void RefreshVisibility()
    {
        foreach (var option in Options)
        {
            if (option.Option.VisibleWhen is { } condition)
            {
                var source = Options.OfType<ChoiceOptionViewModel>().First(c => c.Option.Key == condition.Key);
                option.IsVisible = condition.Values.Contains(source.Selected.Value);
            }
        }
    }

    public static void OpenFolder(string? path)
    {
        if (path is not null && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path } })?.Dispose();
        }
    }
}
