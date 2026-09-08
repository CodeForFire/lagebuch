using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
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
    private readonly Action _onRemoved;

    public IncidentFileRow(
        Guid id,
        string fileName,
        string displayName,
        string sizeDisplay,
        string addedAtDisplay,
        string addedBy,
        bool isImage,
        bool isReadOnly,
        Action<string?> onRenamed,
        Action onRemoved)
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
        _onRemoved = onRemoved;
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

    private bool CanRemove => !IsReadOnly;

    /// <summary>Takes the attachment back completely (#262 UX follow-up). A closed Einsatz is a
    /// historical record: inert rather than throwing, same rule as renaming. The body guard also
    /// covers a programmatic Execute, which bypasses CanExecute — mirrors ForceRow.Remove.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (!CanRemove)
        {
            return;
        }

        _onRemoved();
    }
}

public sealed partial class FilesViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly IFileDialogService _dialogs;
    private readonly Action _onChanged;
    private readonly Action<string, Action> _requestConfirm;

    /// <summary>
    /// <paramref name="requestConfirm"/> asks the host to confirm a destructive action before
    /// running it (message, then the action to run on confirmation) — mirrors
    /// <see cref="ForcesViewModel"/>'s Kraft-removal confirm. Defaults to running the action
    /// immediately, so tests that don't care about the confirmation step don't need to supply one.
    /// </summary>
    public FilesViewModel(
        IIncidentSession session,
        IFileDialogService dialogs,
        Action onChanged,
        Action<string, Action>? requestConfirm = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _dialogs = dialogs;
        _onChanged = onChanged;
        _requestConfirm = requestConfirm ?? ((_, onConfirmed) => onConfirmed());
        IsReadOnly = session.IsReadOnly;
        Files = new ObservableCollection<IncidentFileRow>();
        _session.Changed += Sync;
        Sync();
    }

    public void Dispose() => _session.Changed -= Sync;

    public bool IsReadOnly { get; }

    public static string MaxFileSizeHint => $"Max. {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB pro Datei";

    public ObservableCollection<IncidentFileRow> Files { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFileCommand))]
    private bool _isUploading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Reconciles the rendered rows against <see cref="Incident.Files"/> by id: an attachment can
    /// now be removed by any client (#262 UX follow-up), so the old append-only tail-sync (assuming
    /// <c>Incident.Files</c> only grows, mirroring <c>EtbViewModel</c>'s journal-only idiom) would
    /// leave a removed row stuck on screen. Rows whose id vanished are dropped; new ones are
    /// inserted at the top; everything else keeps its identity and position — still newest-first.
    /// </summary>
    public void Sync()
    {
        var files = _session.Incident.Files;
        var currentIds = new HashSet<Guid>(files.Select(f => f.Id));

        for (var i = Files.Count - 1; i >= 0; i--)
        {
            if (!currentIds.Contains(Files[i].Id))
            {
                Files.RemoveAt(i);
            }
        }

        var renderedIds = new HashSet<Guid>(Files.Select(f => f.Id));
        foreach (var f in files)
        {
            if (!renderedIds.Contains(f.Id))
            {
                Files.Insert(0, ToRow(f));
            }
        }
    }

    private bool CanAddFile => !IsReadOnly && !IsUploading;

    [RelayCommand(CanExecute = nameof(CanAddFile))]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Domain guards, IO and network failures are heterogeneous; all surface as one error line.")]
    private async Task AddFileAsync()
    {
        var path = await _dialogs.PickAttachmentAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ErrorMessage = null;

        var sizeBytes = new FileInfo(path).Length;
        if (sizeBytes > IncidentFile.MaxSizeBytes)
        {
            ErrorMessage = $"Datei ist größer als das Limit von {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB.";
            return;
        }

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

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Domain guards and disk-delete failures are heterogeneous; both surface as one error line, matching AddFileAsync.")]
    private async Task RemoveFileAsync(Guid fileId)
    {
        ErrorMessage = null;
        try
        {
            await _session.RemoveFileAsync(fileId);
            _onChanged(); // Changed already ran Sync(); this only refreshes LastSavedAt et al.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
        f.Id,
        f.FileName,
        f.DisplayName,
        FormatSize(f.SizeBytes),
        Formatting.Timestamp(f.AddedAt),
        f.AddedBy,
        f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
        IsReadOnly,
        displayName => _session.RenameFile(f.Id, displayName),
        () => _requestConfirm(
            $"„{f.DisplayName}“ entfernen. Fortfahren?",
            () => _ = RemoveFileAsync(f.Id)));

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    // The picker already restricts choice to IncidentFile.AllowedContentTypes' extensions; this
    // just maps the chosen local path back to the MIME type the domain expects.
    private static string ContentTypeFor(string path) => IncidentFile.GetMimeType(path, IncidentFile.DefaultMimeType);
}
