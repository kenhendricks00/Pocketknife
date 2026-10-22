using Toolbox.Core.Tools;

namespace Toolbox.App.ViewModels;

public abstract class OptionViewModel(ToolOption option) : Observable
{
    private bool _isVisible = true;

    public ToolOption Option => option;

    public string Label => option.Label;

    public string? Help => option.Help;

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public abstract object Value { get; }

    public static OptionViewModel Create(ToolOption option) => option switch
    {
        ChoiceOption c => new ChoiceOptionViewModel(c),
        NumberOption n => new NumberOptionViewModel(n),
        ToggleOption t => new ToggleOptionViewModel(t),
        TextOption t => new TextOptionViewModel(t),
        _ => throw new NotSupportedException(option.GetType().Name),
    };
}

public sealed class ChoiceOptionViewModel(ChoiceOption option) : OptionViewModel(option)
{
    private Choice _selected = option.Choices.First(c => c.Value == option.Default);

    public IReadOnlyList<Choice> Choices => option.Choices;

    public Choice Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public override object Value => Selected.Value;
}

public sealed class NumberOptionViewModel(NumberOption option) : OptionViewModel(option)
{
    private string _text = option.Default.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public string? Unit => option.Unit;

    public override object Value => Text;
}

public sealed class ToggleOptionViewModel(ToggleOption option) : OptionViewModel(option)
{
    private bool _isOn = option.Default;

    public bool IsOn
    {
        get => _isOn;
        set => Set(ref _isOn, value);
    }

    public override object Value => IsOn;
}

public sealed class TextOptionViewModel(TextOption option) : OptionViewModel(option)
{
    private string _text = option.Default;

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool Multiline => option.Multiline;

    public string? Placeholder => option.Placeholder;

    public override object Value => Text;
}
