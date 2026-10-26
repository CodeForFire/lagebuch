using System.Globalization;
using Avalonia.Data;
using LageBuch.App.Shared.Converters;

namespace LageBuch.Acceptance.Tests;

public class OptionalCountConverterTests
{
    private static readonly OptionalCountConverter Converter = OptionalCountConverter.Instance;

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    public void An_empty_box_reads_as_null_and_digits_as_their_count(string text, int? expected) =>
        Assert.Equal(expected, Converter.ConvertBack(text, typeof(int?), null, CultureInfo.InvariantCulture));

    [Fact]
    public void A_count_too_large_for_an_int_is_reported_as_invalid()
    {
        var result = Converter.ConvertBack("99999999999", typeof(int?), null, CultureInfo.InvariantCulture);

        var notification = Assert.IsType<BindingNotification>(result);
        Assert.Equal(BindingErrorType.DataValidationError, notification.ErrorType);
    }

    [Fact]
    public void A_count_shows_as_its_digits_and_null_as_an_empty_box()
    {
        Assert.Equal("7", Converter.Convert(7, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, Converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
