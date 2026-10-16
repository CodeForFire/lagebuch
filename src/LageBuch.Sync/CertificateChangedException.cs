namespace LageBuch.Sync;

/// <summary>
/// Thrown when a host presents a key this client has not trusted for that address, so the
/// Lagebuchführer has to compare its <see cref="Kennung"/> with the host's screen before trusting it.
/// Either it differs from the key trusted before (§ P0 #2) — the host keeps its key across shares
/// and restarts, so another device answers at that address: a different laptop, a host that lost
/// its key file, or a man-in-the-middle — or nothing was trusted for the address yet
/// (<see cref="IsFirstContact"/>, #597). First contact is no exception to the comparison: whoever
/// answers first on the LAN would otherwise be pinned unasked, and every later connect would look
/// correct.
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

    // The order of the parameters keeps this apart from the public (host, pin, kennung, bool)
    // constructor, which takes the same types.
    private CertificateChangedException(bool isFirstContact, string message, string presentedPin, string kennung)
        : base(message)
    {
        IsFirstContact = isFirstContact;
        PresentedPin = presentedPin;
        Kennung = kennung;
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
    /// True when nothing was trusted for the address yet, false when a trusted key was replaced.
    /// Only the wording differs: either way exactly <see cref="PresentedPin"/> is offered to trust.
    /// </summary>
    public bool IsFirstContact { get; }

    /// <summary>
    /// The first connection to <paramref name="host"/> (#597): nothing is trusted for it yet, so its
    /// Kennung is shown to compare, on a line of its own like a changed key's. A host that shows no
    /// Kennung (<paramref name="hostShowsKennung"/> false) is still asked about rather than trusted
    /// silently: that flag is read from the certificate subject, which whoever answers chooses, so a
    /// silent path for older hosts would be a silent path for anyone. The message then says that
    /// there is nothing to compare and the host wants updating.
    /// </summary>
    public static CertificateChangedException FirstContact(string host, string presentedPin, string kennung, bool hostShowsKennung) =>
        new(isFirstContact: true, FirstContactMessage(host, kennung, hostShowsKennung), presentedPin, kennung);

    /// <summary>
    /// A key whose Kennung equals the trusted one's while the key itself differs. Only a key ground
    /// to match can do that, so there is nothing to compare and nothing to trust: no
    /// <see cref="PresentedPin"/>, and so no trust button.
    /// </summary>
    public static CertificateChangedException Forged(string host) =>
        new($"{host} gibt sich mit einer nachgemachten Kennung als bekannter Host aus. "
            + "Nicht verbinden: Im Netz ist ein fremdes Gerät aktiv.");

    private static string FirstContactMessage(string host, string kennung, bool hostShowsKennung) =>
        hostShowsKennung
            ? $"Erste Verbindung zu {host}. Seine Kennung:\n{kennung}\n"
              + "Vergleiche sie am Host: Ein Klick auf dessen PIN zeigt seine Kennung. "
              + "Nur bei Übereinstimmung vertrauen – sonst antwortet ein fremdes Gerät."
            : $"Erste Verbindung zu {host}. Dort läuft eine ältere Lagebuch-Version, "
              + "die keine Kennung anzeigt – vergleichen ist nicht möglich. Den Host bitte aktualisieren; "
              + "bis dahin nur vertrauen, wenn kein fremdes Gerät im Netz sein kann.";
}
