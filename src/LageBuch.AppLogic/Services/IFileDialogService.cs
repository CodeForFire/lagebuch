using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

public interface IFileDialogService
{
    /// <summary>
    /// <paramref name="initialFolder"/> is a hint only (e.g. the last folder a save succeeded to);
    /// implementations that have no concept of a picker location (Android's app-managed storage)
    /// ignore it.
    /// </summary>
    Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null);

    Task<string?> PickOpenAsync();

    Task<string?> PickExportPdfAsync(string suggestedFileName);

    Task<string?> PickImportJsonAsync();

    Task<string?> PickExportJsonAsync(string suggestedFileName);

    /// <summary>
    /// Lets the user pick one image or PDF to attach to an incident. Returns a local filesystem
    /// path (Android copies the picked content:// URI into app-private storage first, preserving
    /// the original file name) — the caller reads the bytes and infers the content type from the
    /// extension, same as every other pick* method here.
    /// </summary>
    Task<string?> PickAttachmentAsync();

    /// <summary>Opens a local file with the OS's/platform's default viewer for its type.</summary>
    Task OpenFileAsync(string path);

    /// <summary>Opens an http(s) URL in the OS's default browser.</summary>
    [SuppressMessage("Design", "CA1054", Justification = "URLs are free-form launch strings end-to-end (persisted master data, test data); System.Uri would reject non-parseable values and force churn in every caller.")]
    Task OpenUrlAsync(string url);

    /// <summary>
    /// Opens the device's mail app on a new message to <paramref name="address"/> — a bare address
    /// ("a.b@c.de"), never a URI.
    /// </summary>
    /// <remarks>
    /// Taking the address rather than a "mailto:..." string is the point: the scheme is this
    /// implementation's to add, which makes it structurally impossible for a caller to smuggle a
    /// different one through. Contrast <see cref="OpenUrlAsync"/>, which takes a whole URL and
    /// therefore has to defend itself with <see cref="HttpUrlValidator"/> in every implementation.
    /// The address is still validated here as well as at the call site — see
    /// <see cref="MailAddressValidator"/> for what a CR/LF or a '?' in one would otherwise do.
    /// </remarks>
    Task OpenMailAsync(string address);

    /// <summary>
    /// Opens the device's dialer prefilled with <paramref name="number"/>, a bare number written as
    /// the roster writes it ("01 71 / 6 53 58 23"). Never places the call itself.
    /// </summary>
    /// <remarks>
    /// Same contract as <see cref="OpenMailAsync"/>: the scheme belongs to the implementation, and
    /// <see cref="PhoneNumberValidator"/> both normalizes the separators away and refuses anything
    /// that is not a plain number.
    /// </remarks>
    Task OpenPhoneAsync(string number);

    /// <summary>
    /// Offers a written file to the user for hand-off (share sheet, "reveal in folder", or a no-op
    /// where the destination the user already picked via <see cref="PickExportPdfAsync"/>/
    /// <see cref="PickExportJsonAsync"/> is itself the final destination). Called once the file at
    /// <paramref name="path"/> has been fully written.
    /// </summary>
    Task ShareFileAsync(string path, string mimeType);
}
