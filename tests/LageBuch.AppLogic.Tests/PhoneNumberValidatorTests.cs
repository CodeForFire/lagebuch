using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

[SuppressMessage(
    "Design",
    "CA1054",
    Justification = "The theories feed deliberately malformed strings; a parsed Uri cannot express them.")]
public class PhoneNumberValidatorTests
{
    // The roster writes numbers for a human to read. None of those separators may appear in a
    // tel: URI (RFC 3966 allows digits, a leading '+' and the visual separators only), and they
    // carry no dialing meaning, so they are dropped rather than encoded.
    [Theory]
    [InlineData("01 71 / 6 53 58 23", "tel:01716535823")]
    [InlineData("(0 81 41) 12 34-56", "tel:08141123456")]
    [InlineData("0171-6535823", "tel:01716535823")]
    [InlineData("0171.6535823", "tel:01716535823")]
    [InlineData("  0171 6535823  ", "tel:01716535823")]
    public void The_visual_separators_a_roster_number_carries_are_stripped(string input, string expected)
    {
        Assert.True(PhoneNumberValidator.TryGetTelUri(input, out var uri));
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    // The '+' is a literal in a tel: URI; "%2B" is dialed as three characters by some clients.
    [Fact]
    public void A_leading_plus_survives_unencoded()
    {
        Assert.True(PhoneNumberValidator.TryGetTelUri("+49 171 6535823", out var uri));
        Assert.Equal("tel:+491716535823", uri.AbsoluteUri);
        Assert.Equal("tel", uri.Scheme);
    }

    [Fact]
    public void A_non_breaking_space_is_a_separator_too()
    {
        Assert.True(PhoneNumberValidator.TryGetTelUri("0171 6535823", out var uri));
        Assert.Equal("tel:01716535823", uri.AbsoluteUri);
    }

    [Theory]

    // A tel: carrying a USSD/MMI code is the classic abuse of the scheme.
    [InlineData("*#06#")]
    [InlineData("*21*1234#")]
    [InlineData("0171#123")]

    // DTMF pause and the RFC 3966 parameter separator would append an extension or a
    // phone-context nobody typed.
    [InlineData("0171,123")]
    [InlineData("0171;phone-context=+49")]

    // Not a number at all.
    [InlineData("Notruf 112 waehlen")]
    [InlineData("tel:0171")]
    [InlineData("javascript:alert(1)")]
    [InlineData("+49+171")]
    [InlineData("01\r\n71 6535823")]
    [InlineData("0171\u0000")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]

    // Too short to be a number, or implausibly long.
    [InlineData("12")]
    [InlineData("1")]
    [InlineData("123456789012345678901")]
    public void A_dangerous_or_malformed_number_is_refused(string? number)
    {
        Assert.False(PhoneNumberValidator.TryGetTelUri(number, out _));
    }
}
