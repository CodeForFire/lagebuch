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
    /// Offers a written file to the user for hand-off (share sheet, "reveal in folder", or a no-op
    /// where the destination the user already picked via <see cref="PickExportPdfAsync"/>/
    /// <see cref="PickExportJsonAsync"/> is itself the final destination). Called once the file at
    /// <paramref name="path"/> has been fully written.
    /// </summary>
    Task ShareFileAsync(string path, string mimeType);
}
