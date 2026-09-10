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
    private async Task AddFileAsync()
    {
        var path = await _dialogs.PickAttachmentAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await AddFilesAsync(new[] { path });
    }

    /// <summary>
    /// Uploads any number of local paths in one batch — the shared core both the file-picker command
    /// above and the Files tab's drag-and-drop drop handler (code-behind, #262 UX follow-up) call
    /// into. The body guard also covers a programmatic call, which bypasses AddFileCommand's
    /// CanExecute — mirrors IncidentFileRow.Remove.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Domain guards, IO and network failures are heterogeneous; all surface as one error line.")]
    public async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0 || !CanAddFile)
        {
            return;
        }

        ErrorMessage = null;
        IsUploading = true;
        var failures = new List<(string FileName, string Reason)>();
        try
        {
            foreach (var path in paths)
            {
                var sizeBytes = new FileInfo(path).Length;
                if (sizeBytes > IncidentFile.MaxSizeBytes)
                {
                    failures.Add((Path.GetFileName(path), $"Datei ist größer als das Limit von {IncidentFile.MaxSizeBytes / (1024 * 1024)} MB."));
                    continue;
                }

                try
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    await _session.AddFileAsync(Path.GetFileName(path), ContentTypeFor(path), bytes);
                }
                catch (Exception ex)
                {
                    // Domain guards (closed incident, unsupported type, over the size cap) and — once
                    // joined-client upload lands — network failures all surface here rather than crashing.
                    failures.Add((Path.GetFileName(path), ex.Message));
                }
            }

            _onChanged(); // Changed already ran Sync(); this only refreshes LastSavedAt et al.
        }
        finally
        {
            IsUploading = false;
        }

        ErrorMessage = ComposeErrorMessage(paths.Count, failures);
    }

    // A single dropped/picked file keeps the plain historical wording; the "„name“: reason" framing
    // and "N von M" summary only kick in once more than one file was involved, so a multi-file drop's
    // batch report doesn't leave the reader guessing which file a bare reason refers to.
    private static string? ComposeErrorMessage(int total, List<(string FileName, string Reason)> failures)
    {
        if (failures.Count == 0)
        {
            return null;
        }

        if (total == 1)
        {
            return failures[0].Reason;
        }

        var lines = failures.Select(f => $"„{f.FileName}“: {f.Reason}");
        return failures.Count == total
            ? "Keine Datei hinzugefügt:\n" + string.Join("\n", lines)
            : $"{total - failures.Count} von {total} Dateien hinzugefügt. Fehler:\n" + string.Join("\n", lines);
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

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Pull, temp-write and launcher failures are heterogeneous; all surface as one error line, matching AddFileAsync/RemoveFileAsync.")]
    [RelayCommand]
    private async Task OpenFileAsync(IncidentFileRow row)
    {
        ErrorMessage = null;
        try
        {
            var bytes = await _session.GetFileBytesAsync(row.Id);
            if (bytes is null)
            {
                ErrorMessage = $"„{row.DisplayName}“ ist nicht verfügbar.";
                return;
            }

            // A private directory per open, never the shared system temp directory: the name comes
            // from whichever device added the file (a joined client picks it), and two incidents
            // carrying the same file name used to overwrite each other's copy here.
            var tempPath = Path.Combine(AttachmentTempPaths.CreateOpenDirectory(), row.FileName);
            await File.WriteAllBytesAsync(tempPath, bytes);
            await _dialogs.OpenFileAsync(tempPath);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // A block body, not the expression-bodied `new(...)` this used to be: the remove-confirm
    // closure below needs to read the *current* display name at click time, not the DisplayName
    // captured from `f` at construction -- Sync() deliberately never rebuilds an untouched row just
    // because its name changed (that's the point of id-based reconciliation), so `f.DisplayName`
    // would otherwise go stale the moment the row is renamed. `row` is assigned before the
    // constructor call it's captured in returns, but the closure only reads it later, once Remove()
    // actually runs -- by then `row` is set.
    private IncidentFileRow ToRow(IncidentFile f)
    {
        IncidentFileRow row = null!;
        row = new IncidentFileRow(
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
                $"„{row.DisplayName}“ entfernen. Fortfahren?",
                () => _ = RemoveFileAsync(f.Id)));
        return row;
    }

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
