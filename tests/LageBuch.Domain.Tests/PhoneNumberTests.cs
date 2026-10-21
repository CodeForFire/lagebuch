namespace LageBuch.Domain.Tests;

// #700: one form to read (DIN 5008) and one to dial (RFC 3966 global number). The stored value
// never changes; both are derived from whatever was typed.
public class PhoneNumberTests
{
    [Theory]

    // Mobile: the Vorwahl is split off however the number was typed.
    [InlineData("01712345678", "0171 2345678")]
    [InlineData("0171 2345678", "0171 2345678")]
    [InlineData("0171-2345678", "0171 2345678")]
    [InlineData("0171/2345678", "0171 2345678")]
    [InlineData("01 71 / 1 23 45 67", "0171 1234567")]
    [InlineData("(0171) 2345678", "0171 2345678")]
    [InlineData("0176 123 456 78", "0176 12345678")]

    // Landline: libphonenumber knows how long each Vorwahl is, so no separator has to be typed.
    [InlineData("0891234567", "089 1234567")]
    [InlineData("089/1234567", "089 1234567")]
    [InlineData("(089) 1234567", "089 1234567")]
    [InlineData("08141123456", "08141 123456")]
    [InlineData("0 81 41 12 34 56", "08141 123456")]
    [InlineData("03520312345", "035203 12345")]
    [InlineData("030 12345678", "030 12345678")]

    // A Durchwahl after the last '-' stays "-NN".
    [InlineData("089 1234567-12", "089 1234567-12")]
    [InlineData("089/1234567 - 12", "089 1234567-12")]

    // Typed with a country code: shown international, with the trunk 0 and "(0)" dropped.
    [InlineData("+49 171 2345678", "+49 171 2345678")]
    [InlineData("+491712345678", "+49 171 2345678")]
    [InlineData("0049 171 2345678", "+49 171 2345678")]
    [InlineData("+49 (0)171 2345678", "+49 171 2345678")]
    [InlineData("+49891234567", "+49 89 1234567")]
    [InlineData("+49 89 1234567-12", "+49 89 1234567-12")]
    [InlineData("+43 1 234567", "+43 1 234567")]
    [InlineData("0043 1 234567", "+43 1 234567")]
    public void A_number_reads_the_DIN_5008_way(string typed, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Display(typed));
    }

    [Theory]

    // Not a valid number on its own: a short number, an extension, a stub.
    [InlineData("112")]
    [InlineData("1234-56")]
    [InlineData("6535823")]
    [InlineData("0171")]
    [InlineData("01711")]
    [InlineData("0")]

    // Not (only) a number: libphonenumber is never asked to dig one out of the text.
    [InlineData("Zentrale 089 1234567")]
    [InlineData("0171 2345678 (privat)")]
    [InlineData("*#06#")]
    [InlineData("0171,123")]
    [InlineData("+49+171")]
    public void Anything_unrecognised_is_shown_exactly_as_typed(string typed)
    {
        Assert.Equal(typed, PhoneNumber.Display(typed));
    }

    // The input cap is 64 characters: a longer value is not a number this formats.
    [Theory]
    [InlineData(54, "089 1234567")]
    [InlineData(55, null)]
    public void A_number_longer_than_the_cap_is_shown_as_typed(int spaces, string? expected)
    {
        var typed = "089" + new string(' ', spaces) + "1234567";

        Assert.Equal(expected ?? typed, PhoneNumber.Display(typed));
        Assert.Equal(expected is not null, PhoneNumber.TryGetGlobalNumber(typed, out _));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  0171 2345678  ", "0171 2345678")]
    [InlineData("  Zentrale  ", "Zentrale")]
    public void Surrounding_whitespace_is_not_part_of_the_number(string? typed, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Display(typed));
    }

    // The split is a reading aid. Whatever it does, the digits a Lagebuchführer reads back are the
    // ones that were typed, in the same order.
    [Theory]
    [InlineData("01 71 / 1 23 45 67")]
    [InlineData("089/1234567 - 12")]
    [InlineData("03520312345")]
    public void Formatting_never_loses_or_reorders_a_digit(string typed)
    {
        Assert.Equal(Digits(typed), Digits(PhoneNumber.Display(typed)));
    }

    [Theory]
    [InlineData("0171 2345678", "+491712345678")]
    [InlineData("01 71 / 1 23 45 67", "+491711234567")]
    [InlineData("089 1234567-12", "+4989123456712")]
    [InlineData("0891234567", "+49891234567")]
    [InlineData("+49 171 2345678", "+491712345678")]
    [InlineData("+49 (0)171 2345678", "+491712345678")]
    [InlineData("0049 171 2345678", "+491712345678")]
    [InlineData("+43 1 234567", "+431234567")]
    [InlineData("0043 1 234567", "+431234567")]
    [InlineData(" 0171 2345678 ", "+491712345678")]
    public void The_dialable_form_is_an_RFC_3966_global_number(string typed, string expected)
    {
        Assert.True(PhoneNumber.TryGetGlobalNumber(typed, out var global));
        Assert.Equal(expected, global);
    }

    [Theory]

    // No Vorwahl and no country code: there is no way to make it global, so no link is offered.
    // libphonenumber alone would read "6535823" as 06535 823, a number nobody typed.
    [InlineData("112")]
    [InlineData("1234-56")]
    [InlineData("6535823")]
    [InlineData("891234567")]

    // An international prefix with no country code behind it.
    [InlineData("+0171 2345678")]
    [InlineData("000171 2345678")]

    // Not a number: letters, a USSD/MMI code, a DTMF pause, a URI parameter, another scheme.
    [InlineData("Zentrale 089 1234567")]
    [InlineData("*#06#")]
    [InlineData("0171#123")]
    [InlineData("0171,123")]
    [InlineData("0171;phone-context=+49")]
    [InlineData("0171 2345678;ext=12")]
    [InlineData("tel:0171 2345678")]
    [InlineData("javascript:alert(1)")]
    [InlineData("0171 2345678\r\nhttp://example.org")]
    [InlineData("+49+171")]
    [InlineData("0171\u00002345678")]

    // Too short to be a number, or implausibly long.
    [InlineData("0171")]
    [InlineData("0891")]
    [InlineData("01234567890123456789012")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_number_that_cannot_be_dialled_globally_has_no_dialable_form(string? typed)
    {
        Assert.False(PhoneNumber.TryGetGlobalNumber(typed, out var global));
        Assert.Null(global);
    }

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());
}
