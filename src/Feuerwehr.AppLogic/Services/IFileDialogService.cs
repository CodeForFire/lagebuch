namespace Feuerwehr.AppLogic.Services;

public interface IFileDialogService
{
    Task<string?> PickSaveAsync(string suggestedFileName);
    Task<string?> PickOpenAsync();
    Task<string?> PickExportPdfAsync(string suggestedFileName);
}
