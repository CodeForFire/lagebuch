using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LageBuch.App.Shared.Converters;

/// <summary>
/// An ARGB colour as AppLogic carries it (a Kartenebene's fill, which has no Avalonia type to be)
/// to the brush a legend's swatch is painted with.
/// </summary>
public sealed class ArgbBrushConverter : IValueConverter
{
    public static readonly ArgbBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is uint argb ? new ImmutableSolidColorBrush(Color.FromUInt32(argb)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
