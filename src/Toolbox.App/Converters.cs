using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Toolbox.App;

/// <summary>Visible when the value is "set": non-null, a non-empty string, a non-zero count, or true.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSet = value switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            int n => n > 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        return isSet != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
