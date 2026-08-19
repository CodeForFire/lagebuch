namespace Feuerwehr.AppLogic.Services;

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

    /// <summary>
    /// Offers a written file to the user for hand-off (share sheet, "reveal in folder", or a no-op
    /// where the destination the user already picked via <see cref="PickExportPdfAsync"/>/
    /// <see cref="PickExportJsonAsync"/> is itself the final destination). Called once the file at
    /// <paramref name="path"/> has been fully written.
    /// </summary>
    Task ShareFileAsync(string path, string mimeType);
}
