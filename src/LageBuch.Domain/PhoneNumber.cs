using System.Diagnostics.CodeAnalysis;

namespace LageBuch.Domain;

/// <summary>
/// The two forms of a typed phone number (#700): one to read, one to dial. Both are derived on the
/// fly; the number is always stored exactly as it was typed.
/// </summary>
/// <remarks>
/// <para>
/// The reading form follows DIN 5008, pragmatically: "0171 2345678", "+49 171 2345678",
/// "089 1234567-12". The Vorwahl is only split off where it is certain: a German mobile prefix
/// (015x, 016x, 017x, always four digits with the 0), or a separator the Lagebuchführer already
/// typed between Vorwahl and Rufnummer. A landline Vorwahl runs from three to six digits, so
/// "0891234567" cannot be split without guessing and is left alone. Anything not recognised is
/// shown exactly as typed: a digit is never dropped, added or moved, apart from the trunk 0 after
/// +49 ("+49 (0)171 …").
/// </para>
/// <para>
/// The dialing form is an RFC 3966 global number, "+491712345678": a national number takes +49
/// for its leading 0, and 00 becomes +. A number with neither a Vorwahl nor a country code
/// ("112", an internal extension) has no global form, so nothing offers to dial it.
/// </para>
/// </remarks>
public static class PhoneNumber
{
    private const int MaxInputLength = 64;

    // Country code plus at least a short national number; E.164 caps at 15, the slack is for a
    // Durchwahl written onto the end.
    private const int MinGlobalDigits = 6;
    private const int MaxGlobalDigits = 20;

    private const int MobileVorwahlLength = 4;
    private const int MinMobileDigits = 7;
    private const int MinVorwahlLength = 3;
    private const int MaxVorwahlLength = 6;

    /// <summary>
    /// The number as it should be read, or the trimmed input where it is not a number this
    /// recognises. Empty for a blank or missing number.
    /// </summary>
    public static string Display(string? input)
    {
        var raw = input?.Trim() ?? string.Empty;
        if (!IsNumberText(raw))
        {
            return raw;
        }

        if (TryStripGermanCountryCode(raw, out var rest))
        {
            return TryFormatNational("0" + rest, out var national)
                ? "+49 " + national[1..]
                : "+49 " + rest;
        }

        return TryFormatNational(raw, out var formatted) ? formatted : raw;
    }

    /// <summary>
    /// The RFC 3966 global number ("+491712345678") a tel: URI is built from, or <c>false</c>
    /// where the input cannot be made into one.
    /// </summary>
    public static bool TryGetGlobalNumber([NotNullWhen(true)] string? input, [NotNullWhen(true)] out string? global)
    {
        global = null;
        var raw = input?.Trim() ?? string.Empty;
        if (!IsNumberText(raw))
        {
            return false;
        }

        string digits;
        if (TryStripGermanCountryCode(raw, out var rest))
        {
            digits = "49" + Digits(rest);
        }
        else if (raw[0] == '+')
        {
            digits = Digits(raw);
        }
        else
        {
            var all = Digits(raw);
            if (all.StartsWith("00", StringComparison.Ordinal))
            {
                digits = all[2..];
            }
            else if (all.StartsWith('0'))
            {
                digits = "49" + all[1..];
            }
            else
            {
                return false;
            }
        }

        // A country code never starts with 0: "+0171" is a national number with a stray '+'.
        if (digits.Length < MinGlobalDigits || digits.Length > MaxGlobalDigits || digits[0] == '0')
        {
            return false;
        }

        global = "+" + digits;
        return true;
    }

    // Digits, a leading '+', and the separators people put between groups. Anything else -- a
    // letter, '*', '#', ',', ';', a control character, a '+' anywhere but first -- means this is
    // not (only) a number, and it is shown as typed and never dialed.
    private static bool IsNumberText(string raw)
    {
        if (raw.Length == 0 || raw.Length > MaxInputLength)
        {
            return false;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (!char.IsAsciiDigit(c) && !IsSeparator(c) && c is not ('(' or ')' or '/' or '-') && !(c == '+' && i == 0))
            {
                return false;
            }
        }

        return raw.Any(char.IsAsciiDigit);
    }

    private static bool IsSeparator(char c) => c is ' ' or ' ' or '.';

    // "+49 …" or "0049 …" with the country code and any trunk 0 ("(0)", or a stray "0") removed.
    private static bool TryStripGermanCountryCode(string raw, [NotNullWhen(true)] out string? rest)
    {
        rest = null;
        string after;
        if (raw.StartsWith("+49", StringComparison.Ordinal))
        {
            after = raw[3..];
        }
        else if (raw.StartsWith("0049", StringComparison.Ordinal))
        {
            after = raw[4..];
        }
        else
        {
            return false;
        }

        after = after.TrimStart(' ', ' ');
        if (after.StartsWith("(0)", StringComparison.Ordinal))
        {
            after = after[3..].TrimStart(' ', ' ');
        }
        else if (after.StartsWith('0'))
        {
            after = after[1..];
        }

        if (!after.Any(char.IsAsciiDigit))
        {
            return false;
        }

        rest = after;
        return true;
    }

    // A national number ("0…") split into "Vorwahl Rufnummer[-Durchwahl]".
    private static bool TryFormatNational(string text, [NotNullWhen(true)] out string? formatted)
    {
        formatted = null;
        var digits = Digits(text);
        if (digits.Length < MinVorwahlLength || digits[0] != '0' || digits[1] == '0')
        {
            return false;
        }

        if (IsMobile(digits))
        {
            if (digits.Length < MinMobileDigits)
            {
                return false;
            }

            formatted = digits[..MobileVorwahlLength] + " " + digits[MobileVorwahlLength..];
            return true;
        }

        var boundary = VorwahlBoundary(text);
        if (boundary < 0)
        {
            return false;
        }

        var vorwahl = Digits(text[..boundary]);
        var rest = text[(boundary + 1)..];
        if (vorwahl.Length is < MinVorwahlLength or > MaxVorwahlLength
            || vorwahl[0] != '0'
            || rest.IndexOfAny(['(', ')', '/']) >= 0
            || rest.Count(c => c == '-') > 1)
        {
            return false;
        }

        var dash = rest.IndexOf('-', StringComparison.Ordinal);
        var rufnummer = Digits(dash < 0 ? rest : rest[..dash]);
        var durchwahl = dash < 0 ? string.Empty : Digits(rest[(dash + 1)..]);
        if (rufnummer.Length == 0 || (dash >= 0 && durchwahl.Length == 0))
        {
            return false;
        }

        formatted = durchwahl.Length == 0 ? $"{vorwahl} {rufnummer}" : $"{vorwahl} {rufnummer}-{durchwahl}";
        return true;
    }

    private static bool IsMobile(string digits) =>
        digits.StartsWith("015", StringComparison.Ordinal)
        || digits.StartsWith("016", StringComparison.Ordinal)
        || digits.StartsWith("017", StringComparison.Ordinal);

    // Where the typed Vorwahl ends: a closing parenthesis, else a slash, else the first separator
    // or hyphen after the first digit. -1 where nothing was typed between the two.
    private static int VorwahlBoundary(string text)
    {
        var close = text.IndexOf(')', StringComparison.Ordinal);
        if (close >= 0)
        {
            var open = text.IndexOf('(', StringComparison.Ordinal);
            var opensOnce = open >= 0 && open < close && text.LastIndexOf('(') == open && text.LastIndexOf(')') == close;

            // Only a Vorwahl may stand in the parentheses: "0(89) …" after +49, or "(089) …".
            return opensOnce && text[..open].All(char.IsAsciiDigit) ? close : -1;
        }

        if (text.Contains('(', StringComparison.Ordinal))
        {
            return -1;
        }

        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            return slash;
        }

        var seenDigit = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsAsciiDigit(text[i]))
            {
                seenDigit = true;
            }
            else if (seenDigit && (IsSeparator(text[i]) || text[i] == '-'))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());
}
