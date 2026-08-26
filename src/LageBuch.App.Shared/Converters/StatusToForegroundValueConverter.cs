using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LageBuch.App.Shared.Converters;

public sealed class StatusToForegroundValueConverter : IValueConverter
{
    public static readonly StatusToForegroundValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
