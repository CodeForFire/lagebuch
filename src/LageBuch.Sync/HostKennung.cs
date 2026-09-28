using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LageBuch.Sync;

/// <summary>
/// A sync host's identity as the client pins it and as a person compares it. The pin is the SHA-256
/// of the certificate's public key (SPKI), not of the certificate: the host issues a new certificate
/// per share but keeps its key, so re-sharing — a new incident, a restarted app — keeps the pin. The
/// Kennung is the first 60 bits of that hash, short enough to read off the host's screen and long
/// enough that grinding a key to match one takes years rather than hours.
/// </summary>
public static class HostKennung
{
    /// <summary>
    /// Marks a key pin in the trust store. A stored value without it is a whole-certificate hash
    /// from before the persistent host key.
    /// </summary>
    public const string PinPrefix = "SPKI:";

    /// <summary>
    /// The subject of every certificate a host with a persistent key issues. It tells a client that
    /// the host shows a Kennung to compare against; an older host minted a random subject per share
    /// and shows none. Not a security claim — anyone can copy it, and the Kennung comparison still
    /// decides.
    /// </summary>
    public const string CertificateSubject = "CN=LageBuch-Sync-Host";

    // Crockford's base32: no I, L, O or U, so nothing on the screen can be misread as another character.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int Groups = 3;

    private const int CharsPerGroup = 4;

    /// <summary>The value a client stores in its trust store for this certificate's host.</summary>
    public static string Pin(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var spki = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        return PinPrefix + Convert.ToHexString(SHA256.HashData(spki));
    }

    /// <summary>The Kennung of this certificate's host, e.g. <c>7K2Q-M9XD-4HPA</c>.</summary>
    public static string Of(X509Certificate2 certificate) => FromPin(Pin(certificate));

    /// <summary>The Kennung for a pin produced by <see cref="Pin"/>.</summary>
    public static string FromPin(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        // The first 64 bits of the hash, of which the top 60 are spelled out, 5 bits per character.
        var value = ulong.Parse(pin.AsSpan(PinPrefix.Length, 16), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var kennung = new StringBuilder((Groups * CharsPerGroup) + Groups - 1);
        for (var i = 0; i < Groups * CharsPerGroup; i++)
        {
            if (i > 0 && i % CharsPerGroup == 0)
            {
                kennung.Append('-');
            }

            kennung.Append(Alphabet[(int)((value >> (59 - (5 * i))) & 31)]);
        }

        return kennung.ToString();
    }

    /// <summary>Whether the certificate comes from a host that shows its Kennung.</summary>
    public static bool ShowsKennung(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return string.Equals(certificate.Subject, CertificateSubject, StringComparison.Ordinal);
    }
}
