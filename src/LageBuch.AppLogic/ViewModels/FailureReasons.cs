using System.Net.Sockets;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Turns a sharing or export failure into a fixed German sentence that says what to do (#655).
/// </summary>
/// <remarks>
/// The exception's own text never reaches the screen: it is English socket or file-system
/// wording the Lagebuchführer can do nothing with, it may carry a path, and the status line it
/// would land in is read on a tablet with no hover to show the rest. Each method walks the whole
/// <see cref="Exception.InnerException"/> chain, because Kestrel reports a taken port as an
/// <see cref="IOException"/> wrapping the <see cref="SocketException"/> that names it.
/// </remarks>
public static class FailureReasons
{
    public const string SharingPortInUse =
        "Freigabe fehlgeschlagen: Port belegt – teilt ein anderes Lagebuch-Fenster schon? Dort beenden.";

    public const string SharingNoNetwork = "Freigabe fehlgeschlagen: kein Netzwerk – WLAN oder LAN prüfen.";

    public const string SharingAccessDenied = "Freigabe fehlgeschlagen: Zugriff verweigert – Firewall und Rechte prüfen.";

    public const string SharingFailed = "Freigabe fehlgeschlagen – erneut versuchen oder Lagebuch neu starten.";

    public const string ExportAccessDenied = "Export fehlgeschlagen: keine Schreibrechte – anderen Ordner wählen.";

    public const string ExportFolderMissing = "Export fehlgeschlagen: Ordner nicht gefunden – anderen Ordner wählen.";

    public const string ExportFileInUse = "Export fehlgeschlagen: Datei ist geöffnet – PDF schließen, erneut exportieren.";

    public const string ExportDiskFull = "Export fehlgeschlagen: Datenträger voll – anderen Speicherort wählen.";

    public const string ExportWriteFailed = "Export fehlgeschlagen: Datei nicht schreibbar – anderen Ordner wählen.";

    public const string ExportFailed = "Export fehlgeschlagen: PDF nicht erstellt – erneut versuchen.";

    // Win32 errors as HRESULTs, and ENOSPC, which .NET on Unix reports as the raw errno.
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int NoSpaceLeft = 28;

    /// <summary>The reason to show when starting to share the incident failed.</summary>
    public static string ForSharing(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var chain = Chain(failure).ToList();
        if (chain.OfType<SocketException>().FirstOrDefault() is { } socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => SharingPortInUse,
                SocketError.AccessDenied => SharingAccessDenied,
                _ => SharingNoNetwork,
            };
        }

        return chain.Any(e => e is UnauthorizedAccessException) ? SharingAccessDenied : SharingFailed;
    }

    /// <summary>The reason to show when building or writing the PDF failed.</summary>
    public static string ForExport(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        foreach (var e in Chain(failure))
        {
            switch (e)
            {
                case UnauthorizedAccessException:
                    return ExportAccessDenied;
                case DirectoryNotFoundException:
                    return ExportFolderMissing;
                case IOException io:
                    return io.HResult switch
                    {
                        SharingViolation or LockViolation => ExportFileInUse,
                        DiskFull or HandleDiskFull or NoSpaceLeft => ExportDiskFull,
                        _ => ExportWriteFailed,
                    };
            }
        }

        return ExportFailed;
    }

    private static IEnumerable<Exception> Chain(Exception failure)
    {
        for (var e = failure; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }
}
