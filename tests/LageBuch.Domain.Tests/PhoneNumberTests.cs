namespace LageBuch.Domain.Tests;

// #700: one form to read (DIN 5008, pragmatic) and one to dial (RFC 3966 global number). The
// stored value never changes; both are derived from whatever was typed.
public class PhoneNumberTests
{
    [Theory]

    // German mobile prefixes (015x, 016x, 017x): the four-digit Vorwahl is known, so the split is
    // made even when nothing was typed between it and the Rufnummer.
    [InlineData("01712345678", "0171 2345678")]
    [InlineData("0171 2345678", "0171 2345678")]
    [InlineData("0151 23456789", "0151 23456789")]
    [InlineData("015123456789", "0151 23456789")]
    [InlineData("01601234567", "0160 1234567")]
    [InlineData("0176 123 456 78", "0176 12345678")]
    [InlineData("0171-2345678", "0171 2345678")]
    [InlineData("0171/2345678", "0171 2345678")]
    [InlineData("01 71 / 1 23 45 67", "0171 1234567")]
    [InlineData("(0171) 2345678", "0171 2345678")]
    [InlineData("0171234", "0171 234")]

    // Landline: split only where the Lagebuchführer already separated Vorwahl and Rufnummer, and
    // that separator becomes a single space.
    [InlineData("089 1234567", "089 1234567")]
    [InlineData("089/1234567", "089 1234567")]
    [InlineData("089 / 123 45 67", "089 1234567")]
    [InlineData("089-1234567", "089 1234567")]
    [InlineData("(089) 1234567", "089 1234567")]
    [InlineData("08141   123456", "08141 123456")]
    [InlineData("033203 12345", "033203 12345")]

    // A Durchwahl after the last '-' stays "-NN".
    [InlineData("089 1234567-12", "089 1234567-12")]
    [InlineData("089/1234567 - 12", "089 1234567-12")]
    [InlineData("089-1234567-12", "089 1234567-12")]
    [InlineData("(0 81 41) 12 34-56", "08141 1234-56")]

    // International, Germany: +49 and 0049 both read "+49 " and the national number without its 0.
    [InlineData("+49 171 2345678", "+49 171 2345678")]
    [InlineData("+491712345678", "+49 171 2345678")]
    [InlineData("0049 171 2345678", "+49 171 2345678")]
    [InlineData("+49 (0)171 2345678", "+49 171 2345678")]
    [InlineData("+49 (0) 89 1234567-12", "+49 89 1234567-12")]
    [InlineData("+49 89 1234567", "+49 89 1234567")]
    [InlineData("+49 (89) 1234567", "+49 89 1234567")]
    [InlineData("+49 0171 2345678", "+49 171 2345678")]

    // The country code is certain even where the Vorwahl is not, so it is still split off.
    [InlineData("+49891234567", "+49 891234567")]
    [InlineData("004989 1234567", "+49 89 1234567")]
    public void A_number_reads_the_DIN_5008_way(string typed, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Display(typed));
    }

    [Theory]

    // A landline with nothing between Vorwahl and Rufnummer: German Vorwahlen run from three to
    // six digits, so any split would be a guess.
    [InlineData("0891234567")]
    [InlineData("08141123456")]

    // Not a German national number, or not a number at all: shown as typed, never reordered.
    [InlineData("112")]
    [InlineData("1234-56")]
    [InlineData("+43 1 234567")]
    [InlineData("0043 1 234567")]
    [InlineData("Zentrale 089 1234567")]
    [InlineData("0171 2345678 (privat)")]
    [InlineData("*#06#")]
    [InlineData("0171,123")]
    [InlineData("0 81 41 12 34 56")]
    [InlineData("0812345678 / 12")]
    [InlineData("089 12-34-56")]
    [InlineData("089 1234567-")]
    [InlineData("01711")]
    [InlineData("+49+171")]

    // Too short, or a Vorwahl longer than the six digits any German one has.
    [InlineData("0")]
    [InlineData("08")]
    [InlineData("017123")]
    [InlineData("0331234 12345")]

    // Separators that do not mark off a Vorwahl.
    [InlineData("/089 1234567")]
    [InlineData("089/-12")]
    [InlineData("(089 1234567")]
    [InlineData("089) 1234567")]
    [InlineData("((089) 1234567")]
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
    // ones that were typed, in the same order -- only the trunk 0 after +49 is dropped.
    [Theory]
    [InlineData("01 71 / 1 23 45 67")]
    [InlineData("(0 81 41) 12 34-56")]
    [InlineData("089/1234567 - 12")]
    [InlineData("0151 23456789")]
    public void Formatting_never_loses_or_reorders_a_digit(string typed)
    {
        Assert.Equal(Digits(typed), Digits(PhoneNumber.Display(typed)));
    }

    [Theory]
    [InlineData("0171 2345678", "+491712345678")]
    [InlineData("01 71 / 1 23 45 67", "+491711234567")]
    [InlineData("089 1234567-12", "+4989123456712")]
    [InlineData("(0 81 41) 12 34-56", "+498141123456")]
    [InlineData("0891234567", "+49891234567")]
    [InlineData("+49 171 2345678", "+491712345678")]
    [InlineData("+49 (0)171 2345678", "+491712345678")]
    [InlineData("+49 0171 2345678", "+491712345678")]
    [InlineData("0049 171 2345678", "+491712345678")]
    [InlineData("+43 1 234567", "+431234567")]
    [InlineData("0043 1 234567", "+431234567")]
    [InlineData(" 0171 2345678 ", "+491712345678")]

    // Six digits is the shortest global number, twenty the longest.
    [InlineData("08912", "+498912")]
    [InlineData("089 1234567890123456", "+49891234567890123456")]
    public void The_dialable_form_is_an_RFC_3966_global_number(string typed, string expected)
    {
        Assert.True(PhoneNumber.TryGetGlobalNumber(typed, out var global));
        Assert.Equal(expected, global);
    }

    [Theory]

    // No Vorwahl and no country code: there is no way to make it global, so no link is offered.
    [InlineData("112")]
    [InlineData("1234567")]
    [InlineData("1234-56")]

    // An international prefix with no country code behind it.
    [InlineData("+0171 2345678")]
    [InlineData("000171 2345678")]

    // Not a number: letters, a USSD/MMI code, a DTMF pause, a URI parameter, another scheme.
    [InlineData("Zentrale 089 1234567")]
    [InlineData("*#06#")]
    [InlineData("0171#123")]
    [InlineData("0171,123")]
    [InlineData("0171;phone-context=+49")]
    [InlineData("tel:0171 2345678")]
    [InlineData("javascript:alert(1)")]
    [InlineData("0171 2345678\r\nhttp://example.org")]
    [InlineData("+49+171")]
    [InlineData("0171\u00002345678")]

    // Too short to be a number, or implausibly long.
    [InlineData("0171")]
    [InlineData("0891")]
    [InlineData("089 12345678901234567")]
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
