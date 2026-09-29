namespace LageBuch.Sync;

/// <summary>
/// Thrown when a host presents a different key than the one this client previously trusted for that
/// address (Trust-on-First-Use violation, § P0 #2). The host keeps its key across shares and
/// restarts, so this means another device answers at that address — a different laptop, a host
/// that lost its key file, or a man-in-the-middle. Surfaced to the user with the new
/// <see cref="Kennung"/> to compare against the host's screen before trusting it.
/// </summary>
public sealed class CertificateChangedException : Exception
{
    /// <summary>
    /// Builds the German message around <paramref name="kennung"/>. The Kennung stands on a line of
    /// its own: wrapped at its hyphen it no longer reads like what the host shows, and comparing the
    /// two is the whole point of the message. A host that does not show its Kennung
    /// (<paramref name="hostShowsKennung"/> false) runs an older Lagebuch, which mints a new key per
    /// share; the message says so instead of sending the user to look for something that is not there.
    /// </summary>
    public CertificateChangedException(string host, string presentedPin, string kennung, bool hostShowsKennung = true)
        : base(hostShowsKennung
            ? $"{host} meldet sich mit einer neuen Kennung:\n{kennung}\n"
              + "Vergleiche sie am Host: Ein Klick auf dessen PIN zeigt seine Kennung. "
              + "Stimmt sie nicht überein, nicht verbinden – dann antwortet ein fremdes Gerät."
            : $"{host} meldet sich mit einem neuen Schlüssel und zeigt keine Kennung an: "
              + "Dort läuft eine ältere Lagebuch-Version. Den Host aktualisieren; "
              + "bis dahin nur vertrauen, wenn er gerade neu gestartet wurde.")
    {
        PresentedPin = presentedPin;
        Kennung = kennung;
    }

    public CertificateChangedException(string message)
        : base(message)
    {
    }

    public CertificateChangedException()
        : this("Die Kennung des Hosts hat sich geändert.")
    {
    }

    public CertificateChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The pin of the key the host just presented; trusting it lets exactly that key in. Null when it must not be trusted.</summary>
    public string? PresentedPin { get; }

    /// <summary>The Kennung of the key the host just presented, as the host shows it.</summary>
    public string? Kennung { get; }

    /// <summary>
    /// A key whose Kennung equals the trusted one's while the key itself differs. Only a key ground
    /// to match can do that, so there is nothing to compare and nothing to trust: no
    /// <see cref="PresentedPin"/>, and so no trust button.
    /// </summary>
    public static CertificateChangedException Forged(string host) =>
        new($"{host} gibt sich mit einer nachgemachten Kennung als bekannter Host aus. "
            + "Nicht verbinden: Im Netz ist ein fremdes Gerät aktiv.");
}
