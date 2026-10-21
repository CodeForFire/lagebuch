using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;

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
    /// <summary>
    /// True only for a number this app is willing to dial, with <paramref name="uri"/> set to the
    /// ready-to-launch RFC 3966 global number "tel:+&lt;digits&gt;" — see
    /// <see cref="PhoneNumber.TryGetGlobalNumber"/> for how a national number gets its +49, and
    /// why a number with neither a Vorwahl nor a country code ("112") is refused (#700).
    /// </summary>
    /// <remarks>
    /// The '+' is left unencoded on purpose: it is a literal in a tel: URI, and "%2B" is dialed as
    /// three characters by some clients.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1054",
        Justification = "A string is the input by design: deciding whether an unvalidated, possibly malformed string is a dialable number is this method's job, so requiring a parsed System.Uri would push that decision back onto every caller.")]
    public static bool TryGetTelUri([NotNullWhen(true)] string? input, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        // The scheme is added here, to a '+' and digits: nothing typed can choose another one.
        return PhoneNumber.TryGetGlobalNumber(input, out var global)
            && Uri.TryCreate("tel:" + global, UriKind.Absolute, out uri)
            && string.Equals(uri.Scheme, "tel", StringComparison.Ordinal);
    }
}
