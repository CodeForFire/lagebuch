namespace LageBuch.Sync;

/// <summary>
/// Thrown when a host presents a different TLS certificate than the one this client previously
/// trusted for that address (Trust-on-First-Use violation, § P0 #2) — typically a host restart with
/// a new ephemeral cert, or a man-in-the-middle. Surfaced to the user as a trust-reset prompt.
/// </summary>
public sealed class CertificateChangedException : Exception
{
    public CertificateChangedException(string host)
        : base($"Das Zertifikat von {host} hat sich geändert. "
             + "Duplikat oder man-in-the-middle? Wenn der Host neu gestartet wurde, "
             + "lösche die gespeicherte Vertrauenszuordnung für dieses Gerät und versuche es erneut.")
    {
    }

    public CertificateChangedException()
        : this("unbekannt")
    {
    }

    public CertificateChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
