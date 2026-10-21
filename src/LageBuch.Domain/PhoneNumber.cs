using System.Diagnostics.CodeAnalysis;
using PhoneNumbers;
using ParsedNumber = PhoneNumbers.PhoneNumber;

namespace LageBuch.Domain;

/// <summary>
/// The two forms of a typed phone number (#700): one to read, one to dial. Both are derived on the
/// fly; the number is always stored exactly as it was typed.
/// </summary>
/// <remarks>
/// <para>
/// Parsing and formatting are libphonenumber's, whose metadata knows every German Vorwahl, from
/// "089" to "035203". The reading form is DIN 5008: "0171 2345678", "089 1234567-12", and
/// "+49 171 2345678" or "+43 1 234567" for a number typed with its country code. A number
/// libphonenumber does not accept as valid is shown exactly as typed.
/// </para>
/// <para>
/// The dialing form is an RFC 3966 global number, "+491712345678". A number with neither a Vorwahl
/// nor a country code ("112", an internal extension) is not valid on its own, so it has no global
/// form and nothing offers to dial it.
/// </para>
/// </remarks>
public static class PhoneNumber
{
    private const string DefaultRegion = "DE";
    private const int MaxInputLength = 64;

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// The number as it should be read, or the trimmed input where it is not a number this
    /// recognises. Empty for a blank or missing number.
    /// </summary>
    public static string Display(string? input)
    {
        var raw = input?.Trim() ?? string.Empty;
        if (!TryParse(raw, out var number))
        {
            return raw;
        }

        // A Durchwahl is written "-12" (DIN 5008). libphonenumber reads it as part of the number,
        // so it is split off where what stands before the last '-' is a valid number of its own;
        // otherwise the '-' was only a separator, as in "0171-2345678".
        var durchwahl = string.Empty;
        var dash = raw.LastIndexOf('-');
        if (dash > 0
            && TryParse(raw[..dash], out var head)
            && IsAllDigits(raw[(dash + 1)..].Trim()))
        {
            number = head;
            durchwahl = "-" + raw[(dash + 1)..].Trim();
        }

        // A number keeps the form it was typed in: national stays national, +49 stays +49.
        var format = number.CountryCodeSource == ParsedNumber.Types.CountryCodeSource.FROM_DEFAULT_COUNTRY
            ? PhoneNumberFormat.NATIONAL
            : PhoneNumberFormat.INTERNATIONAL;
        return Util.Format(number, format) + durchwahl;
    }

    /// <summary>
    /// The RFC 3966 global number ("+491712345678") a tel: URI is built from, or <c>false</c>
    /// where the input cannot be made into one.
    /// </summary>
    public static bool TryGetGlobalNumber([NotNullWhen(true)] string? input, [NotNullWhen(true)] out string? global)
    {
        global = null;
        if (!TryParse(input?.Trim() ?? string.Empty, out var number))
        {
            return false;
        }

        global = Util.Format(number, PhoneNumberFormat.E164);
        return true;
    }

    // Only digits, a leading '+' and the separators people put between groups are handed to
    // libphonenumber, which would otherwise dig a number out of "Zentrale 089 1234567" or accept
    // "*#06#" and ";ext=": anything else is not (only) a number, and it is shown as typed and never
    // dialed. A number has to be valid, not merely possible, to count.
    private static bool TryParse(string raw, [NotNullWhen(true)] out ParsedNumber? number)
    {
        number = null;
        if (raw.Length == 0 || raw.Length > MaxInputLength || !raw.Any(char.IsAsciiDigit))
        {
            return false;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            if (!char.IsAsciiDigit(raw[i])
                && raw[i] is not (' ' or ' ' or '.' or '(' or ')' or '/' or '-')
                && !(raw[i] == '+' && i == 0))
            {
                return false;
            }
        }

        try
        {
            // libphonenumber takes a national number without its trunk 0 too, so "6535823" would
            // become 06535 823. Here a number with neither the 0 nor a country code is local.
            var parsed = Util.ParseAndKeepRawInput(raw, DefaultRegion);
            var national = parsed.CountryCodeSource == ParsedNumber.Types.CountryCodeSource.FROM_DEFAULT_COUNTRY;
            if ((national && raw.First(char.IsAsciiDigit) != '0') || !Util.IsValidNumber(parsed))
            {
                return false;
            }

            number = parsed;
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }

    private static bool IsAllDigits(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);
}
