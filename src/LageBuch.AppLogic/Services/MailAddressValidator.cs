using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The address check shared by every <see cref="IFileDialogService.OpenMailAsync"/> caller and
/// implementation. The companion to <see cref="HttpUrlValidator"/>, for the one other scheme this
/// app is willing to hand to a platform launcher.
/// </summary>
/// <remarks>
/// <para>
/// A roster address crosses a trust boundary: it comes out of an imported Stammdaten JSON, not out
/// of the operator's keyboard, and it ends up in a URI that the platform hands to whatever app
/// registered for <c>mailto:</c>. Two things follow.
/// </para>
/// <para>
/// First, a CR or LF in an address is a header-injection attempt. Some mail clients parse the
/// mailto: they are handed line by line, so "a@b.de%0ABcc: opfer@x" silently adds a recipient the
/// operator never saw. Every control character is refused outright.
/// </para>
/// <para>
/// Second, mailto: carries a query string ("?subject=&amp;body=&amp;bcc="). An address containing
/// '?', '&amp;' or '#' would compose a message nobody wrote, so those are refused — as is '%',
/// which would let the same characters be spelled out in encoded form.
/// </para>
/// <para>
/// The rule is therefore an allowlist, not a denylist: only what a Feuerwehr address actually
/// contains gets through. It is deliberately narrower than RFC 5321 — quoted local parts, IP
/// literal domains and non-punycode internationalized domains are all refused. A brigade needing
/// one of those can still type it into their mail client by hand; the cost of getting this wrong
/// is a message sent somewhere nobody intended. Deliberately not
/// <c>System.Net.Mail.MailAddress</c>, which accepts <c>"a b"@c.de</c> and display-name forms
/// like <c>Max &lt;a@b.de&gt;</c>.
/// </para>
/// <para>
/// Hand-rolled rather than a regular expression: no backtracking surface on an attacker-supplied
/// string, and nothing for the analyzers' <c>[GeneratedRegex]</c> rule to argue with.
/// </para>
/// </remarks>
public static class MailAddressValidator
{
    private const int MaxTotalLength = 254;
    private const int MaxLocalLength = 64;

    /// <summary>
    /// True only for an address this app is willing to launch, with <paramref name="uri"/> set to
    /// the ready-to-launch "mailto:&lt;address&gt;".
    /// </summary>
    /// <remarks>
    /// No percent-encoding is applied and none is needed: every character with a meaning in a
    /// mailto: URI is refused above, so the concatenation cannot change the URI's structure.
    /// Escaping instead of refusing would leave us reasoning about whether every downstream client
    /// decodes the same way — and a client that decodes <c>%0A</c> before parsing would still be
    /// injectable.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1054",
        Justification = "A string is the input by design: deciding whether an unvalidated, possibly malformed string is a usable address is this method's job, so requiring a parsed System.Uri would push that decision back onto every caller.")]
    public static bool TryGetMailtoUri(string? input, out Uri uri)
    {
        uri = null!;
        var address = input?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > MaxTotalLength)
        {
            return false;
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0
            || at != address.LastIndexOf('@')
            || at > MaxLocalLength
            || at == address.Length - 1)
        {
            return false;
        }

        return IsLocalPart(address.AsSpan(0, at))
            && IsDomain(address.AsSpan(at + 1))
            && Uri.TryCreate("mailto:" + address, UriKind.Absolute, out uri!)
            && string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dot-atom, minus every RFC-legal character that also means something in a mailto: URI.
    /// No leading, trailing or doubled dot.
    /// </summary>
    private static bool IsLocalPart(ReadOnlySpan<char> s)
    {
        if (s[0] == '.' || s[^1] == '.' || s.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '+' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// At least two labels of ASCII letters, digits and hyphens, none of them empty or
    /// hyphen-edged, and a TLD of two or more letters. "xn--" punycode passes; a literal umlaut
    /// domain does not.
    /// </summary>
    private static bool IsDomain(ReadOnlySpan<char> s)
    {
        if (s.Length > 253 || s[0] == '.' || s[^1] == '.')
        {
            return false;
        }

        var labels = 0;
        var lastDot = -1;
        for (var i = 0; i <= s.Length; i++)
        {
            if (i != s.Length && s[i] != '.')
            {
                continue;
            }

            var label = s[(lastDot + 1)..i];
            if (!IsLabel(label))
            {
                return false;
            }

            lastDot = i;
            labels++;
        }

        return labels >= 2 && IsTld(s[(s.LastIndexOf('.') + 1)..]);
    }

    private static bool IsLabel(ReadOnlySpan<char> label)
    {
        if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (var c in label)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTld(ReadOnlySpan<char> tld)
    {
        if (tld.Length < 2)
        {
            return false;
        }

        foreach (var c in tld)
        {
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }
        }

        return true;
    }
}
