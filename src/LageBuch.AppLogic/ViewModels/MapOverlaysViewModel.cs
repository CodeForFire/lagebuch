using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.Files;
using LageBuch.Domain.Map;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The EBENEN beside the Lagekarte: the GeoJSON files imported into the Einsatz, each switched on
/// or off. Which Ebene is shown, and at which time, belongs to this screen and is not stored, like
/// the HYDRANTEN switch; the Ebenen themselves are the Einsatz's and sync. A new Ebene is shown
/// and the ones before it are hidden, so a newer Ausbreitungsprognose replaces the old one on the
/// map while the old one stays on record.
/// </summary>
public sealed partial class MapOverlaysViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IFileDialogService? _dialogs;
    private readonly Action<string, string, Action>? _requestConfirm;
    private readonly IClock _clock;
    private readonly Action<GeoBounds> _showBounds;

    // Read and parsed once per file and session: the file never changes, only whether it is shown.
    private readonly Dictionary<Guid, Task<(OverlayData? Data, string? Error, bool Parsed)>> _loads = [];

    // Every row's load since the last settle, for a caller that has to wait on the files being read.
    private readonly List<Task> _pending = [];

    public MapOverlaysViewModel(
        IIncidentSession session,
        Action onChanged,
        IFileDialogService? dialogs,
        Action<string, string, Action>? requestConfirm,
        IClock clock,
        Action<GeoBounds> showBounds)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(showBounds);
        _session = session;
        _onChanged = onChanged;
        _dialogs = dialogs;
        _requestConfirm = requestConfirm;
        _clock = clock;
        _showBounds = showBounds;
        Refresh();
    }

    /// <summary>The Ebenen, oldest first, as the Einsatz has them.</summary>
    public ObservableCollection<MapOverlayItem> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    /// <summary>Importing needs an open Einsatz and a picker on this platform.</summary>
    public bool CanImport => !_session.IsReadOnly && _dialogs is not null;

    /// <summary>What the map draws: the current frame of every shown Ebene, the newest on top.</summary>
    [ObservableProperty]
    private IReadOnlyList<OverlayShape> _visibleShapes = [];

    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Why the last import or removal failed, in German.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Brings the list in line with the Einsatz, keeping what this screen shows of each Ebene.</summary>
    public void Refresh()
    {
        var overlays = _session.Incident.MapOverlays;
        var known = Items.ToDictionary(i => i.Id);
        var first = Items.Count == 0;
        Items.Clear();
        foreach (var overlay in overlays)
        {
            if (known.TryGetValue(overlay.Id, out var item))
            {
                Items.Add(item);

                // A file not there yet -- a peer's upload still under way -- is looked for again on
                // every change, the upload's own announcement included.
                if (item.IsMissing)
                {
                    Track(LoadAsync(item));
                }

                continue;
            }

            item = new MapOverlayItem(overlay, this);
            Items.Add(item);
            Track(LoadAsync(item));
        }

        // The newest Ebene is shown when it arrives, here or from another device, and hides the
        // ones before it. On opening the Einsatz only the newest is shown.
        if (Items.Count > 0 && (first || !known.ContainsKey(Items[^1].Id)))
        {
            foreach (var item in Items)
            {
                item.SetVisibleQuietly(ReferenceEquals(item, Items[^1]));
            }
        }

        OnPropertyChanged(nameof(HasItems));
        UpdateVisibleShapes();
    }

    private void Track(Task load)
    {
        _pending.RemoveAll(t => t.IsCompleted);
        _pending.Add(load);
    }

    /// <summary>Completes when every Ebene's file has been looked for. Tests wait on it; the view never needs to.</summary>
    internal async Task SettledAsync()
    {
        while (_pending.Count > 0)
        {
            var pending = _pending.ToList();
            _pending.Clear();
            await Task.WhenAll(pending);
        }
    }

    internal void UpdateVisibleShapes() =>
        VisibleShapes = Items.Where(i => i.IsVisible).SelectMany(i => i.CurrentShapes).ToList();

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Picker, read and upload failures are heterogeneous; every one surfaces in Error, as FilesViewModel's do.")]
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanImport || _dialogs is null || IsImporting)
        {
            return;
        }

        Error = null;
        string? path;
        try
        {
            path = await _dialogs.PickMapOverlayAsync();
        }
        catch (Exception ex)
        {
            Error = $"Kartenebene nicht übernommen: {ex.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsImporting = true;
        try
        {
            Error = await ImportFileAsync(path);
        }
        catch (Exception ex)
        {
            Error = $"Kartenebene nicht übernommen: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    // Null when the file became an Ebene, else why not. The file is read and checked before
    // anything is stored, so only a file the Lagekarte can draw joins the Einsatz.
    private async Task<string?> ImportFileAsync(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!IncidentFile.MapOverlayMimeTypesByExtension.TryGetValue(Path.GetExtension(fileName), out var contentType))
        {
            return "Nur GeoJSON-Dateien (.geojson, .json) oder ZIP-Archive davon.";
        }

        if (new FileInfo(path).Length > IncidentFile.MaxSizeBytes)
        {
            return IncidentFile.TooLargeMessage;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var (data, error) = await Task.Run(() => Parse(bytes));
        if (data is null)
        {
            return error;
        }

        await _session.AddMapOverlayAsync(fileName, contentType, bytes, data.Summary(Path.GetFileNameWithoutExtension(fileName)));
        _onChanged();
        _showBounds(data.Bounds);
        return null;
    }

    internal void ConfirmRemove(MapOverlayItem item)
    {
        if (_session.IsReadOnly)
        {
            return;
        }

        void Remove() => _ = RemoveAsync(item.Id);
        if (_requestConfirm is null)
        {
            Remove();
            return;
        }

        _requestConfirm("Kartenebene entfernen?", $"„{item.Name}“ von der Lagekarte und aus dem Einsatz entfernen. Fortfahren?", Remove);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A refused or failed removal surfaces in Error, as FilesViewModel.RemoveFileAsync's does.")]
    private async Task RemoveAsync(Guid overlayId)
    {
        Error = null;
        try
        {
            await _session.RemoveMapOverlayAsync(overlayId);
            _onChanged();
        }
        catch (Exception ex)
        {
            Error = $"Kartenebene nicht entfernt: {ex.Message}";
        }
    }

    internal void ShowBounds(MapOverlayItem item)
    {
        if (item.Data is { } data)
        {
            _showBounds(data.Bounds);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A file that cannot be fetched costs its own Ebene: the failure is shown on that row, never thrown into the map.")]
    private async Task LoadAsync(MapOverlayItem item)
    {
        var fileId = item.FileId;
        if (!_loads.TryGetValue(fileId, out var load))
        {
            load = Fetch(fileId);
            _loads[fileId] = load;
        }

        (OverlayData? Data, string? Error, bool Parsed) result;
        try
        {
            result = await load;
        }
        catch (Exception ex)
        {
            result = (null, ex.Message, false);
        }

        // A file that could not be fetched is tried again; one that was read, or will never parse, stays.
        var missing = result.Data is null && !result.Parsed;
        if (missing && _loads.TryGetValue(fileId, out var cached) && ReferenceEquals(cached, load))
        {
            _loads.Remove(fileId);
        }

        item.Loaded(result.Data, result.Error, missing, _clock.Now);
        UpdateVisibleShapes();
    }

    private async Task<(OverlayData? Data, string? Error, bool Parsed)> Fetch(Guid fileId)
    {
        var bytes = await _session.GetFileBytesAsync(fileId);
        if (bytes is null)
        {
            return (null, "Datei ist noch nicht verfügbar.", false);
        }

        var (data, error) = await Task.Run(() => Parse(bytes));
        return (data, error, true);
    }

    private static (OverlayData? Data, string? Error) Parse(byte[] bytes) =>
        OverlayGeoJson.TryParse(bytes, out var data, out var error) ? (data, null) : (null, error);
}

/// <summary>One Ebene in the EBENEN list.</summary>
public sealed partial class MapOverlayItem : ObservableObject
{
    private readonly MapOverlaysViewModel _owner;
    private bool _quiet;

    internal MapOverlayItem(MapOverlay overlay, MapOverlaysViewModel owner)
    {
        Id = overlay.Id;
        FileId = overlay.FileId;
        Name = overlay.Name;
        Detail = $"{Formatting.Timestamp(overlay.ImportedAt)} · {overlay.ImportedBy}";
        _owner = owner;
    }

    public Guid Id { get; }

    public Guid FileId { get; }

    public string Name { get; }

    /// <summary>When and by whom it was imported.</summary>
    public string Detail { get; }

    public string VisibilityAutomationName => $"{Name}: auf der Karte zeigen";

    public string RemoveAutomationName => $"{Name}: entfernen";

    public string ShowAutomationName => $"{Name}: auf der Karte anzeigen";

    /// <summary>Null while the file is still being read, and when it could not be.</summary>
    public OverlayData? Data { get; private set; }

    public bool IsLoading => Data is null && LoadError is null;

    /// <summary>Why the Ebene cannot be drawn, in German.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private string? _loadError;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Which point in time is shown, for an Ebene that has several.</summary>
    [ObservableProperty]
    private int _frameIndex;

    /// <summary>The shapes of the shown frame; none until the file is read.</summary>
    public IReadOnlyList<OverlayShape> CurrentShapes =>
        Data is { } data ? data.Frames[Math.Clamp(FrameIndex, 0, data.Frames.Count - 1)].Shapes : [];

    internal void SetVisibleQuietly(bool visible)
    {
        _quiet = true;
        IsVisible = visible;
        _quiet = false;
    }

    /// <summary>The file was not there to read; it is looked for again on the next change.</summary>
    public bool IsMissing { get; private set; }

    internal void Loaded(OverlayData? data, string? error, bool missing, DateTimeOffset now)
    {
        IsMissing = missing;
        Data = data;
        FrameIndex = data?.FrameIndexAt(now) ?? 0;
        LoadError = data is null ? $"Ebene konnte nicht gelesen werden: {error}" : null;
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(IsLoading));
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_quiet)
        {
            _owner.UpdateVisibleShapes();
        }
    }

    partial void OnFrameIndexChanged(int value)
    {
        if (IsVisible)
        {
            _owner.UpdateVisibleShapes();
        }
    }

    [RelayCommand]
    private void Remove() => _owner.ConfirmRemove(this);

    [RelayCommand]
    private void Show()
    {
        IsVisible = true;
        _owner.ShowBounds(this);
    }
}
