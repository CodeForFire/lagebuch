using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain.Files;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One row of the Dateien list. <see cref="DisplayName"/> is freely editable and writes through on
/// every change (mirrors <c>ForceRow</c>'s Status/Bemerkung fields); <see cref="FileName"/> stays
/// fixed and is used only for <see cref="FilesViewModel.OpenFileAsync"/>'s temp-file naming, so a
/// display name without a recognizable extension can't break opening the file externally.
/// </summary>
public sealed partial class IncidentFileRow : ObservableObject
{
    private readonly Action<string?> _onRenamed;

    public IncidentFileRow(
        Guid id, string fileName, string displayName, string sizeDisplay, string addedAtDisplay,
        string addedBy, bool isImage, bool isReadOnly, Action<string?> onRenamed)
    {
        Id = id;
        FileName = fileName;
        _displayName = displayName;
        SizeDisplay = sizeDisplay;
        AddedAtDisplay = addedAtDisplay;
        AddedBy = addedBy;
        IsImage = isImage;
        IsReadOnly = isReadOnly;
        _onRenamed = onRenamed;
    }

    public Guid Id { get; }
    public string FileName { get; }
    public string SizeDisplay { get; }
    public string AddedAtDisplay { get; }
    public string AddedBy { get; }
    public bool IsImage { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    private string _displayName;

    partial void OnDisplayNameChanged(string value)
    {
        if (IsReadOnly)
            return;
        _onRenamed(value);
    }
}

public sealed partial class FilesViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly IFileDialogService _dialogs;
    private readonly Action _onChanged;

    // How many of the journal-like, append-only Incident.Files we have already rendered — mirrors
    // EtbViewModel.Sync's tail-append idiom (see there for why: append-only means diffing is just
    // "render whatever's new", and it keeps the collection's identity stable across re-syncs).
    private int _rendered;

    public FilesViewModel(IIncidentSession session, IFileDialogService dialogs, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _dialogs = dialogs;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        Files = new ObservableCollection<IncidentFileRow>();
        _session.Changed += Sync;
        Sync();
    }

    public bool IsReadOnly { get; }
    public ObservableCollection<IncidentFileRow> Files { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFileCommand))]
    private bool _isUploading;

    [ObservableProperty]
    private string? _errorMessage;

    public void Sync()
    {
        var files = _session.Incident.Files;
        for (var i = _rendered; i < files.Count; i++)
            Files.Insert(0, ToRow(files[i]));
        _rendered = files.Count;
    }

    private bool CanAddFile => !IsReadOnly && !IsUploading;

    [RelayCommand(CanExecute = nameof(CanAddFile))]
    private async Task AddFileAsync()
    {
        var path = await _dialogs.PickAttachmentAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        ErrorMessage = null;
        IsUploading = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            await _session.AddFileAsync(Path.GetFileName(path), ContentTypeFor(path), bytes);
            _onChanged(); // Changed already ran Sync(); this only refreshes LastSavedAt et al.
        }
        catch (Exception ex)
        {
            // Domain guards (closed incident, unsupported type, over the size cap) and — once
            // joined-client upload lands — network failures all surface here rather than crashing.
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(IncidentFileRow row)
    {
        ErrorMessage = null;
        var bytes = await _session.GetFileBytesAsync(row.Id);
        if (bytes is null)
        {
            ErrorMessage = $"„{row.DisplayName}“ ist nicht verfügbar.";
            return;
        }
        var tempPath = Path.Combine(Path.GetTempPath(), row.FileName);
        await File.WriteAllBytesAsync(tempPath, bytes);
        await _dialogs.OpenFileAsync(tempPath);
    }

    private IncidentFileRow ToRow(IncidentFile f) => new(
        f.Id, f.FileName, f.DisplayName, FormatSize(f.SizeBytes), Formatting.Timestamp(f.AddedAt), f.AddedBy,
        f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase), IsReadOnly,
        displayName => _session.RenameFile(f.Id, displayName));

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };

    // Mirrors IncidentFile.AllowedContentTypes' extensions — the picker already restricts choice to
    // these, this just maps the chosen local path back to the MIME type the domain expects.
    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
