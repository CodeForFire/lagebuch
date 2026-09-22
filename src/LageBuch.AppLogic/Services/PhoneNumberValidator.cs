using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The number check shared by every <see cref="IFileDialogService.OpenPhoneAsync"/> caller and
/// implementation. Normalizes as it validates.
/// </summary>
/// <remarks>
/// <para>
/// A roster number is written for a human to read — "01 71 / 6 53 58 23" — and none of those
/// separators may appear in a tel: URI, which RFC 3966 restricts to digits, a leading '+' and the
/// visual separators. They carry no dialing meaning, so they are dropped rather than encoded.
/// </para>
/// <para>
/// '*' and '#' are refused even though a dialer accepts them: a tel: carrying a USSD/MMI code
/// ("*#06#", or a call-forwarding code) is the classic abuse of this scheme, and a roster number
/// never contains one. ',' (DTMF pause) and ';' (the RFC 3966 parameter separator) are refused for
/// the same reason — they would append an extension or a phone-context nobody typed.
/// </para>
/// </remarks>
public static class PhoneNumberValidator
{
    private const int MinDigits = 3;

    // E.164 caps at 15; the slack is for an internal extension written into the roster.
    private const int MaxDigits = 20;
    private const int MaxInputLength = 64;

    /// <summary>
    /// True only for a number this app is willing to dial, with <paramref name="uri"/> set to the
    /// ready-to-launch "tel:&lt;digits&gt;" — a leading '+' where the input had one, then digits
    /// and nothing else.
    /// </summary>
    /// <remarks>
    /// The '+' is left unencoded on purpose: it is a literal in a tel: URI, and "%2B" is dialed as
    /// three characters by some clients.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1054",
        Justification = "A string is the input by design: deciding whether an unvalidated, possibly malformed string is a dialable number is this method's job, so requiring a parsed System.Uri would push that decision back onto every caller.")]
    public static bool TryGetTelUri(string? input, out Uri uri)
    {
        uri = null!;
        var raw = input?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxInputLength)
        {
            return false;
        }

        var dial = new StringBuilder(raw.Length);
        if (raw[0] == '+')
        {
            dial.Append('+');
            raw = raw[1..];
        }

        var digits = 0;
        foreach (var c in raw)
        {
            if (char.IsAsciiDigit(c))
            {
                dial.Append(c);
                digits++;
            }
            else if (c is not (' ' or '/' or '-' or '.' or '(' or ')' or ' '))
            {
                // Anything else -- a letter, a '+' anywhere but first, '*', '#', ',', ';', a
                // control character -- means this is not a phone number and must not reach a dialer.
                return false;
            }
        }

        return digits >= MinDigits
            && digits <= MaxDigits
            && Uri.TryCreate("tel:" + dial, UriKind.Absolute, out uri!)
            && string.Equals(uri.Scheme, "tel", StringComparison.Ordinal);
    }
}
