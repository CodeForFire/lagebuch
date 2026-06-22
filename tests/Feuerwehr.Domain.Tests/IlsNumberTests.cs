using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Domain.Tests;

public class IlsNumberTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("0001")]
    public void Parse_accepts_exactly_four_digits(string input)
    {
        Assert.Equal(input, IlsNumber.Parse(input).Value);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("")]
    [InlineData("1234\n")]
    [InlineData("1234 ")]
    public void Parse_rejects_non_four_digit_values(string input)
    {
        Assert.Throws<FormatException>(() => IlsNumber.Parse(input));
    }

    [Fact]
    public void TryParse_returns_false_for_invalid()
    {
        Assert.False(IlsNumber.TryParse("xx", out var result));
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_returns_true_for_valid()
    {
        Assert.True(IlsNumber.TryParse("4242", out var result));
        Assert.Equal("4242", result!.Value);
    }
}
