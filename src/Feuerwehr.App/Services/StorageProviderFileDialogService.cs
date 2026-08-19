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
    private static readonly FilePickerFileType Json =
        new("Stammdaten (JSON)") { Patterns = new[] { "*.json" } };

    private readonly Func<TopLevel?> _topLevel;

    public StorageProviderFileDialogService(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null)
    {
        var top = _topLevel();
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Einsatz speichern",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "fwincident",
            FileTypeChoices = new[] { Incident },
            SuggestedStartLocation = await ResolveStartLocation(top, initialFolder)
        });
        return file?.TryGetLocalPath();
    }

    // A missing/moved/inaccessible folder just means no start-location hint — the OS picker falls
    // back to wherever it last remembered, exactly like today's behavior with no hint at all.
    private static async Task<IStorageFolder?> ResolveStartLocation(TopLevel top, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        try
        {
            return await top.StorageProvider.TryGetFolderFromPathAsync(folder);
        }
        catch
        {
            return null;
        }
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

    public async Task<string?> PickImportJsonAsync()
    {
        var top = _topLevel();
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Stammdaten importieren",
            AllowMultiple = false,
            FileTypeFilter = new[] { Json }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickExportJsonAsync(string suggestedFileName)
    {
        var top = _topLevel();
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Stammdaten exportieren",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { Json }
        });
        return file?.TryGetLocalPath();
    }

    // The user already chose the exact destination via the native save dialog above — there is
    // nothing further to hand off on desktop.
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}
