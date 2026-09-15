using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RayaTrainer.App.Converters;

/// <summary>
/// Converts a string to visibility: non-empty -> Visible, null/empty/whitespace -> Collapsed.
/// Used to hide structured info panels until there is something to show.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string text && !string.IsNullOrWhiteSpace(text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
