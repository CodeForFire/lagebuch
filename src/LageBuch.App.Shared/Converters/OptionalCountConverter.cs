using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LageBuch.App.Shared.Converters;

/// <summary>
/// Binds a digits-only count TextBox to an <c>int?</c>, where an empty box is <c>null</c>. Avalonia's
/// default conversion cannot turn "" into an <c>int?</c> and marks the emptied field invalid, and
/// <c>TargetNullValue=''</c> only covers the way in, not the way back.
/// </summary>
public sealed class OptionalCountConverter : IValueConverter
{
    public static readonly OptionalCountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    // A number too large for an int is the one input IntegerOnly lets through; it stays flagged
    // as invalid, as it was before.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : new BindingNotification(new FormatException($"„{text}“ ist keine Anzahl."), BindingErrorType.DataValidationError);
    }
}
