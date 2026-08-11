namespace Feuerwehr.AppLogic.Services;

public interface IFileDialogService
{
    Task<string?> PickSaveAsync(string suggestedFileName);
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
