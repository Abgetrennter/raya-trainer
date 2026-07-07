using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RayaTrainer.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static BoolToVisibilityConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static InverseBoolToVisibilityConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public static NullToCollapsedConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s) return string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class PageIndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int pageIndex && parameter is string targetIndex && int.TryParse(targetIndex, out var target))
        {
            return pageIndex == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
