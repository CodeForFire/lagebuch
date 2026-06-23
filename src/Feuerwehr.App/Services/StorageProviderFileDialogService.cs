using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Feuerwehr.AppLogic.Services;

namespace Feuerwehr.App.Services;

public sealed class StorageProviderFileDialogService : IFileDialogService
{
    private static readonly FilePickerFileType Incident =
        new("Einsatzdokumentation") { Patterns = new[] { "*.fwincident" } };
    private static readonly FilePickerFileType Pdf =
        new("PDF-Dokument") { Patterns = new[] { "*.pdf" } };

    private readonly Func<TopLevel?> _topLevel;

    public StorageProviderFileDialogService(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> PickSaveAsync(string suggestedFileName)
    {
        var top = _topLevel();
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Einsatz speichern",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "fwincident",
            FileTypeChoices = new[] { Incident }
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenAsync()
    {
        var top = _topLevel();
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Einsatz öffnen",
            AllowMultiple = false,
            FileTypeFilter = new[] { Incident }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickExportPdfAsync(string suggestedFileName)
    {
        var top = _topLevel();
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "PDF exportieren",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { Pdf }
        });
        return file?.TryGetLocalPath();
    }
}
