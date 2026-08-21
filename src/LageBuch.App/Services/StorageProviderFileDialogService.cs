using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Services;

public sealed class StorageProviderFileDialogService : IFileDialogService
{
    private static readonly FilePickerFileType Incident =
        new("Einsatzdokumentation") { Patterns = new[] { "*.fwincident" } };
    private static readonly FilePickerFileType Pdf =
        new("PDF-Dokument") { Patterns = new[] { "*.pdf" } };
    private static readonly FilePickerFileType Json =
        new("Stammdaten (JSON)") { Patterns = new[] { "*.json" } };
    private static readonly FilePickerFileType Attachment =
        new("Bild oder PDF") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp", "*.pdf" } };

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

    public async Task<string?> PickAttachmentAsync()
    {
        var top = _topLevel();
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Datei anhängen",
            AllowMultiple = false,
            FileTypeFilter = new[] { Attachment }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task OpenFileAsync(string path)
    {
        LaunchWithOsDefault(path);
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(string url)
    {
        // Belt-and-suspenders: LaunchWithOsDefault below is Process.Start with UseShellExecute=true,
        // which resolves arbitrary URI handlers and even local executable paths, so this refuses
        // anything but http(s) regardless of what a caller passes in.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Task.CompletedTask;

        LaunchWithOsDefault(uri.AbsoluteUri);
        return Task.CompletedTask;
    }

    // UseShellExecute launches the OS's registered default handler on Windows/macOS, for a local
    // path or a URL alike; Linux has no such shell-execute concept in .NET, so xdg-open is the
    // desktop-agnostic equivalent, and it resolves URLs the same way it resolves file paths.
    private static void LaunchWithOsDefault(string target)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{target}\"") { UseShellExecute = false });
    }

    // The user already chose the exact destination via the native save dialog above — there is
    // nothing further to hand off on desktop.
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}
