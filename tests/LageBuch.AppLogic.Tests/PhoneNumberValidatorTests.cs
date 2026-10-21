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
    // carry no dialing meaning, so they are dropped rather than encoded. The national 0 becomes
    // +49 (#700): RFC 3966 only lets a local number through with a phone-context, so the link
    // carries the global number every dialer reads the same way.
    [Theory]
    [InlineData("01 71 / 6 53 58 23", "tel:+491716535823")]
    [InlineData("(0 81 41) 12 34-56", "tel:+498141123456")]
    [InlineData("0171-6535823", "tel:+491716535823")]
    [InlineData("0171.6535823", "tel:+491716535823")]
    [InlineData("  0171 6535823  ", "tel:+491716535823")]
    [InlineData("0049 171 6535823", "tel:+491716535823")]
    [InlineData("+49 (0)171 6535823", "tel:+491716535823")]
    [InlineData("0043 1 234567", "tel:+431234567")]
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
        Assert.Equal("tel:+491716535823", uri.AbsoluteUri);
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
    [InlineData("0171 6535823\r\nhttp://example.org")]
    [InlineData("0171 6535823 sms:0171")]
    [InlineData("+49 171 6535823?body=x")]
    [InlineData("0171 6535823#intent:#Intent;end")]
    [InlineData("+49+171")]
    [InlineData("01\r\n71 6535823")]
    [InlineData("0171\u0000")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]

    // No Vorwahl and no country code: it cannot be made global, so it is not offered (#700).
    [InlineData("112")]
    [InlineData("6535823")]
    [InlineData("+0171 6535823")]

    // Too short to be a number, or implausibly long.
    [InlineData("12")]
    [InlineData("1")]
    [InlineData("0171")]
    [InlineData("0123456789012345678901")]
    public void A_dangerous_or_malformed_number_is_refused(string? number)
    {
        Assert.False(PhoneNumberValidator.TryGetTelUri(number, out _));
    }

    // Whatever was typed, a URI that comes out is a tel: URI holding a '+' and digits, nothing
    // else: the validator owns the scheme, and no input can choose another one or add to it.
    [Theory]
    [InlineData("0171 6535823")]
    [InlineData("+49 (0) 89 1234567-12")]
    [InlineData("(0 81 41) 12 34-56")]
    [InlineData("0043 1 234567")]
    public void A_dialable_number_becomes_a_tel_uri_and_nothing_else(string number)
    {
        Assert.True(PhoneNumberValidator.TryGetTelUri(number, out var uri));
        Assert.Equal("tel", uri.Scheme);
        Assert.Matches("^tel:\\+[1-9][0-9]+$", uri.AbsoluteUri);
    }
}
