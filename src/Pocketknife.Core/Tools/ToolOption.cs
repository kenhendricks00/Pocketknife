using System.Globalization;

namespace Pocketknife.Core.Tools;

/// <summary>Shows an option only while another choice option has one of the given values.</summary>
public sealed record OptionCondition(string Key, IReadOnlyList<string> Values);

public abstract record ToolOption(string Key, string Label)
{
    public string? Help { get; init; }

    public OptionCondition? VisibleWhen { get; init; }

    public abstract object DefaultValue { get; }

    /// <summary>Converts a value from the UI or a caller into this option's canonical type, or throws.</summary>
    public abstract object Coerce(object value);
}

public sealed record Choice(string Value, string Label);

public sealed record ChoiceOption(string Key, string Label, IReadOnlyList<Choice> Choices, string Default) : ToolOption(Key, Label)
{
    public override object DefaultValue => Default;

    public override object Coerce(object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return Choices.Any(c => c.Value == text)
            ? text!
            : throw new ArgumentException($"'{text}' is not a valid choice for {Label}.");
    }
}

public sealed record NumberOption(string Key, string Label, double Default, double Min, double Max) : ToolOption(Key, Label)
{
    public string? Unit { get; init; }

    public bool WholeNumber { get; init; } = true;

    public override object DefaultValue => Default;

    public override object Coerce(object value)
    {
        var number = value switch
        {
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) => parsed,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            string s => throw new ArgumentException($"{Label} must be a number, not '{s}'."),
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
        if (double.IsNaN(number) || number < Min || number > Max)
        {
            throw new ArgumentException($"{Label} must be between {Min} and {Max}.");
        }

        return WholeNumber ? Math.Round(number) : number;
    }
}

public sealed record ToggleOption(string Key, string Label, bool Default) : ToolOption(Key, Label)
{
    public override object DefaultValue => Default;

    public override object Coerce(object value) => value switch
    {
        bool b => b,
        string s => bool.Parse(s),
        _ => throw new ArgumentException($"{Label} must be on or off."),
    };
}

public sealed record TextOption(string Key, string Label, string Default) : ToolOption(Key, Label)
{
    public bool Multiline { get; init; }

    public string? Placeholder { get; init; }

    public override object DefaultValue => Default;

    public override object Coerce(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}

/// <summary>Validated option values for one run, falling back to each option's default.</summary>
public sealed class OptionValues
{
    private readonly Dictionary<string, object> _values;

    public OptionValues(IReadOnlyList<ToolOption> schema, IReadOnlyDictionary<string, object?>? overrides = null)
    {
        _values = schema.ToDictionary(o => o.Key, o => o.DefaultValue);
        foreach (var (key, value) in overrides ?? new Dictionary<string, object?>())
        {
            var option = schema.FirstOrDefault(o => o.Key == key)
                ?? throw new ArgumentException($"Unknown option '{key}'.");
            if (value is not null)
            {
                _values[key] = option.Coerce(value);
            }
        }
    }

    public string Choice(string key) => (string)_values[key];

    public double Number(string key) => (double)_values[key];

    public int WholeNumber(string key) => (int)Math.Round(Number(key));

    public bool Toggle(string key) => (bool)_values[key];

    public string Text(string key) => (string)_values[key];

    public object this[string key] => _values[key];
}
